using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ZLR.VM.Debugging;

namespace ZLR.VM
{
    public delegate bool TimedInputCallback();
    public delegate short CharTranslator(char ch);

    public partial class ZMachine
    {
#pragma warning disable 0169
        internal async Task ReadImplAsync(ushort buffer, ushort parse, ushort time, ushort routine, int retryPC, int nextPC, int resultStorage)
        {
            byte max, initlen;
            if (zversion <= 4)
            {
                max = (byte)(GetByte(buffer) - 1);
                initlen = 0;
            }
            else
            {
                max = GetByte(buffer);
                initlen = GetByte(buffer + 1);
            }

            byte terminator;
            string str;

            if (zversion <= 3)
                ShowStatusImpl();

            BeginExternalWait();
            try
            {
                if (cmdRdr != null && cmdRdr.EOF)
                {
                    cmdRdr.Dispose();
                    cmdRdr = null;
                }

                if (cmdRdr == null)
                {
                    var initial = string.Empty;
                    if (initlen > 0)
                    {
                        // we never get here for V1-4
                        System.Diagnostics.Debug.Assert(ZVersion >= 5);
                        var sb = new StringBuilder(initlen);
                        for (var i = 0; i < initlen; i++)
                            sb.Append(CharFromZSCII(GetByte(buffer + 2 + i)));
                        initial = sb.ToString();
                    }

                    var result = await (time != 0
                        ? TimedReadLineAsync(initial, time, routine, terminatingChars, debugging)
                        : io.ReadLineAsync(initial, terminatingChars, debugging, CancellationToken.None));

                    switch (result.Outcome)
                    {
                        case ReadOutcome.KeyPressed:
                            terminator = result.Terminator;
                            str = result.Text;
                            break;

                        default:
                            throw new DebuggerBreakException(retryPC);
                    }
                }
                else
                {
                    (str, terminator) = await cmdRdr.ReadLineAsync();
                    System.Diagnostics.Debug.Assert(str != null, "str != null");
                    // ReSharper disable once AssignNullToNotNullAttribute
                    io.PutCommand(terminator == 13 ? str + "\n" : str);
                }

                cmdWtr?.WriteLineAsync(str, terminator);
            }
            finally
            {
                EndExternalWait();
            }

            // ReSharper disable once PossibleNullReferenceException
            var chars = StringToZSCII(str.ToLower());
            if (zversion <= 4)
            {
                for (var i = 0; i < Math.Min(chars.Length, max); i++)
                    SetByte(buffer + 1 + i, chars[i]);
                SetByte(buffer + 1 + Math.Min(chars.Length, max), 0);
            }
            else
            {
                SetByte(buffer + 1, (byte)chars.Length);
                for (var i = 0; i < Math.Min(chars.Length, max); i++)
                    SetByte(buffer + 2 + i, chars[i]);
            }

            if (parse != 0)
                Tokenize(buffer, parse, 0, false);

            if (resultStorage >= 0)
            {
                StoreResult((byte)resultStorage, terminator);
            }

            pc = nextPC;
        }

        [NotNull]
        private Task<ReadLineResult> TimedReadLineAsync([NotNull] string initial, ushort time, ushort routine,
            [CanBeNull] byte[] terminatingKeys, bool allowDebuggerBreak, CancellationToken cancellationToken = default)
        {
            return TimedReadAsync(time, routine,
                ct => io.ReadLineAsync(initial, terminatingKeys, allowDebuggerBreak, ct), cancellationToken);
        }

        // ReSharper disable once UnusedParameter.Global
        internal async Task ReadCharImplAsync(ushort time, ushort routine, int retryPC, int nextPC, int resultStorage)
        {
            // TODO: support debugger break in read_char
            short result;

            BeginExternalWait();
            try
            {
                if (cmdRdr != null && cmdRdr.EOF)
                {
                    cmdRdr.Dispose();
                    cmdRdr = null;
                }

                if (cmdRdr == null)
                {
                    result = await (time != 0
                        ? TimedReadKeyAsync(time, routine, c => FilterInput(CharToZSCII(c)))
                        : io.ReadKeyAsync(c => FilterInput(CharToZSCII(c))));
                }
                else
                {
                    result = await cmdRdr.ReadKeyAsync();
                }

                cmdWtr?.WriteKey((byte)result);
            }
            finally
            {
                EndExternalWait();
            }

            if (resultStorage >= 0)
            {
                StoreResult((byte)resultStorage, result);
            }

            pc = nextPC;
        }
#pragma warning restore 0169

        [NotNull]
        private Task<short> TimedReadKeyAsync(ushort time, ushort routine, [NotNull] CharTranslator translator,
            CancellationToken cancellationToken = default)
        {
            return TimedReadAsync(time, routine, ct => io.ReadKeyAsync(translator, ct), cancellationToken);
        }

        [ItemNotNull]
        private async Task<T> TimedReadAsync<T>(ushort time, ushort routine,
            [NotNull] [InstantHandle] Func<CancellationToken, Task<T>> interruptibleReader,
            CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.Assert(time != 0);
            System.Diagnostics.Debug.Assert(routine != 0);

            // canceled when the timer goes off or the read completes
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var readTask = interruptibleReader(cts.Token);
            var delayTask = Task.Delay(time * 100, cts.Token);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var completed = await Task.WhenAny(readTask, delayTask);

                if (completed == readTask)
                {
                    cts.Cancel();
                    return await readTask;
                }

                // must be the timer
                System.Diagnostics.Debug.Assert(completed == delayTask);

                // fault or cancel if necessary, then start a new timer before calling the routine
                await completed;
                delayTask = Task.Delay(time * 100, cts.Token);
                await HandleInputTimerAsync(routine);
            }
        }

        private async Task<bool> HandleInputTimerAsync(ushort routine)
        {
            EnterFunctionImpl((short)routine, null, 0, pc);

            await JitLoopAsync();

            var result = stack.Pop();
            return result != 0;
        }

        private short FilterInput(short ch)
        {
            // only allow characters that are defined for input: section 3.8
            if (ch < 32 && ch != 8 && ch != 13 && ch != 27)
                return 0;
            if (ch >= 127 && (ch <= 128 || ch >= 255))
                return 0;

            return ch;
        }

        private struct Token
        {
            public readonly byte StartPos;
            public readonly byte Length;

            public Token(byte startPos, byte length)
            {
                StartPos = startPos;
                Length = length;
            }
        }

        private bool IsTokenSpace(byte ch)
        {
            return ch == 9 || ch == 32;
        }

        [NotNull]
        private List<Token> SplitTokens([NotNull] byte[] buffer, ushort userDict)
        {
            var result = new List<Token>();
            byte[] seps;

            if (userDict == 0)
            {
                seps = wordSeparators;
            }
            else
            {
                var n = GetByte(userDict);
                seps = new byte[n];
                GetBytes(userDict + 1, n, seps, 0);
            }

            var i = 0;
            do
            {
                // skip whitespace
                while (i < buffer.Length && IsTokenSpace(buffer[i]))
                    i++;

                if (i >= buffer.Length)
                    break;

                // found a separator?
                if (Array.IndexOf(seps, buffer[i]) >= 0)
                {
                    result.Add(new Token((byte)i, 1));
                    i++;
                }
                else
                {
                    var start = (byte)i;

                    // find the end of the word
                    while (i < buffer.Length && !IsTokenSpace(buffer[i]) &&
                            Array.IndexOf(seps, buffer[i]) == -1)
                    {
                        i++;
                    }

                    // add it to the list
                    result.Add(new Token(start, (byte)(i - start)));
                }
            } while (i < buffer.Length);

            return result;
        }

        internal void Tokenize(ushort buffer, ushort parse, ushort userDict, bool skipUnrecognized)
        {
            byte bufLen;
            int tokenOffset;

            if (zversion <= 4)
            {
                bufLen = 0;
                tokenOffset = 1;

                for (var i = buffer + 1; i < romStart; i++)
                    if (GetByte(i) == 0)
                    {
                        bufLen = (byte)(i - buffer - 1);
                        break;
                    }
            }
            else
            {
                bufLen = GetByte(buffer + 1);
                tokenOffset = 2;
            }

            var max = GetByte(parse + 0);
            byte count = 0;

            var myBuffer = new byte[bufLen];
            GetBytes(buffer + tokenOffset, bufLen, myBuffer, 0);
            var tokens = SplitTokens(myBuffer, userDict);

            foreach (var tok in tokens)
            {
                var word = LookUpWord(userDict, myBuffer, tok.StartPos, tok.Length);
                if (word == 0 && skipUnrecognized)
                    continue;

                SetWord(parse + 2 + 4 * count, (short)word);
                SetByte(parse + 2 + 4 * count + 2, tok.Length);
                SetByte(parse + 2 + 4 * count + 3, (byte)(tokenOffset + tok.StartPos));
                count++;

                if (count == max)
                    break;
            }

            SetByte(parse + 1, count);
        }

        private ushort LookUpWord(int userDict, byte[] buffer, int pos, int length)
        {
            int dictStart;

            var word = EncodeText(buffer, pos, length, DictWordSize);

            if (userDict != 0)
            {
                var n = GetByte(userDict);
                dictStart = userDict + 1 + n;
            }
            else
            {
                dictStart = dictionaryTable + 1 + wordSeparators.Length;
            }

            var entryLength = GetByte(dictStart++);

            int entries;
            if (userDict == 0)
                entries = (ushort)GetWord(dictStart);
            else
                entries = GetWord(dictStart);
            dictStart += 2;

            if (entries < 0)
            {
                // use linear search for unsorted user dictionary
                for (var i = 0; i < entries; i++)
                {
                    var addr = dictStart + i * entryLength;
                    if (CompareWords(word, addr) == 0)
                        return (ushort)addr;
                }
            }
            else
            {
                // use binary search
                int start = 0, end = entries;
                while (start < end)
                {
                    var mid = (start + end) / 2;
                    var addr = dictStart + mid * entryLength;
                    var cmp = CompareWords(word, addr);
                    if (cmp == 0)
                        return (ushort)addr;
                    else if (cmp < 0)
                        end = mid;
                    else
                        start = mid + 1;
                }
            }

            return 0;
        }

        private int CompareWords([NotNull] byte[] word, int addr)
        {
            for (var i = 0; i < word.Length; i++)
            {
                var cmp = word[i] - GetByte(addr + i);
                if (cmp != 0)
                    return cmp;
            }

            return 0;
        }

        /// <summary>
        /// Encodes a section of text, optionally truncating or padding the output to a fixed size.
        /// </summary>
        /// <param name="input">The buffer containing the plain text.</param>
        /// <param name="start">The index within <paramref name="input"/> where the
        /// plain text starts.</param>
        /// <param name="length">The length of the plain text.</param>
        /// <param name="numZchars">The number of 5-bit characters that the output should be
        /// truncated or padded to, which must be a multiple of 3; or 0 to allow variable size
        /// output (padded up to a multiple of 2 bytes, if necessary).</param>
        /// <returns>The encoded text, with th.</returns>
        [NotNull]
        private byte[] EncodeText(byte[] input, int start, int length, int numZchars)
        {
            List<byte> zchars;
            if (numZchars == 0)
            {
                zchars = new List<byte>(length);
            }
            else
            {
                if (numZchars < 0 || numZchars % 3 != 0)
                    throw new ArgumentException("Output size must be a multiple of 3", nameof(numZchars));
                zchars = new List<byte>(numZchars);
            }

            for (var i = 0; i < length; i++)
            {
                var zc = input[start + i];
                var ch = CharFromZSCII(zc);

                if (ch == ' ')
                {
                    zchars.Add(0);
                }
                else
                {
                    int alpha;
                    if ((alpha = Array.IndexOf(alphabet0, ch)) >= 0)
                    {
                        zchars.Add((byte)(alpha + 6));
                    }
                    else if ((alpha = Array.IndexOf(alphabet1, ch)) >= 0)
                    {
                        zchars.Add(4);
                        zchars.Add((byte)(alpha + 6));
                    }
                    else if ((alpha = Array.IndexOf(alphabet2, ch)) >= 0)
                    {
                        zchars.Add(5);
                        zchars.Add((byte)(alpha + 6));
                    }
                    else
                    {
                        zchars.Add(5);
                        zchars.Add(6);
                        zchars.Add((byte)(zc >> 5));
                        zchars.Add((byte)(zc & 31));
                    }
                }
            }

            int resultBytes;
            if (numZchars == 0)
            {
                // pad up to a multiple of 3
                while (zchars.Count % 3 != 0)
                    zchars.Add(5);
                resultBytes = zchars.Count * 2 / 3;
            }
            else
            {
                // pad up to the fixed size
                while (zchars.Count < numZchars)
                    zchars.Add(5);
                resultBytes = numZchars * 2 / 3;
            }

            var result = new byte[resultBytes];
            int zi = 0, ri = 0;
            while (ri < resultBytes)
            {
                result[ri] = (byte)((zchars[zi] << 2) | (zchars[zi + 1] >> 3));
                result[ri + 1] = (byte)((zchars[zi + 1] << 5) | zchars[zi + 2]);
                ri += 2;
                zi += 3;
            }

            result[resultBytes - 2] |= 128;
            return result;
        }

        internal async Task SetInputStreamAsync(short num, int nextPC)
        {
            switch (num)
            {
                case 0:
                    if (cmdRdr != null)
                    {
                        cmdRdr.Dispose();
                        cmdRdr = null;
                    }
                    break;

                case 1:
                    var cmdStream = await io.OpenCommandFileAsync(false);
                    if (cmdStream != null)
                    {
                        cmdRdr?.Dispose();

                        try
                        {
                            cmdRdr = new CommandFileReader(cmdStream);
                        }
                        catch
                        {
                            cmdRdr = null;
                        }
                    }
                    break;

                default:
                    throw new Exception("Invalid input stream #" + num);
            }

            pc = nextPC;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player's inputs are to be
        /// written to a command file.
        /// </summary>
        /// <remarks>
        /// <para>This property enables or disables output stream 4.</para>
        /// <para>When this property is set to true, <see cref="IAsyncZMachineIO.OpenCommandFileAsync"/>
        /// will be called to get a stream for the command file. The property will be
        /// reset to false after the game finishes running.</para>
        /// </remarks>
        [PublicAPI]
        public bool WritingCommandsToFile
        {
            get => cmdWtr != null;
            set => SetOutputStreamAsync((short) (value ? 4 : -4), 0, PC).GetAwaiter().GetResult();  // XXX asyncify
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player's inputs are to be
        /// read from a command file.
        /// </summary>
        /// <remarks>
        /// <para>This property switches between input stream 1 (true) and input stream 0
        /// (false).</para>
        /// <para>When this property is set to true, <see cref="IAsyncZMachineIO.OpenCommandFileAsync"/>
        /// will be called to get a stream for the command file. The property will be
        /// reset to false after the game finishes running.</para>
        /// </remarks>
        [PublicAPI]
        public bool ReadingCommandsFromFile
        {
            get => cmdRdr != null;
            set => SetInputStreamAsync((short) (value ? 1 : 0), PC).GetAwaiter().GetResult();       // XXX asyncify
        }

        private class CommandFileReader : IDisposable
        {
            private StreamReader rdr;

            public CommandFileReader([NotNull] Stream stream)
            {
                rdr = new StreamReader(stream);
            }

            public void Dispose()
            {
                if (rdr != null)
                {
                    rdr.Close();
                    rdr = null;
                }
            }

            public bool EOF => rdr.EndOfStream;

            [NotNull]
            public async Task<(string line, byte terminator)> ReadLineAsync()
            {
                byte terminator = 13;
                var line = await rdr.ReadLineAsync();

                if (line != null && line.EndsWith("]"))
                {
                    var idx = line.LastIndexOf('[');
                    if (idx >= 0)
                    {
                        var key = line.Substring(idx + 1, line.Length - idx - 2);
                        if (int.TryParse(key, out var keyCode))
                        {
                            line = line.Substring(0, idx);
                            terminator = (byte)keyCode;
                        }
                    }
                }

                return (line, terminator);
            }

            [NotNull]
            public async Task<byte> ReadKeyAsync()
            {
                var line = await rdr.ReadLineAsync();

                if (string.IsNullOrEmpty(line))
                    return 13;

                if (line.StartsWith("["))
                {
                    var idx = line.IndexOf(']');
                    if (idx >= 0)
                    {
                        var key = line.Substring(1, idx - 1);
                        if (int.TryParse(key, out var keyCode))
                            return (byte)keyCode;
                    }
                }

                return (byte)line[0];
            }
        }

        private class CommandFileWriter : IDisposable
        {
            private StreamWriter wtr;

            public CommandFileWriter([NotNull] Stream stream)
            {
                wtr = new StreamWriter(stream);
            }

            public void Dispose()
            {
                if (wtr != null)
                {
                    wtr.Close();
                    wtr = null;
                }
            }

            public async Task WriteLineAsync(string text, byte terminator)
            {
                await wtr.WriteLineAsync(
                    terminator != 13 || text.EndsWith("]")
                        ? $"{text}[{terminator}]"
                        : text);
            }

            public void WriteKey(byte key)
            {
                if (key < 128 && key != '[')
                    wtr.WriteLine((char)key);
                else
                    wtr.WriteLine("[{0}]", key);
            }
        }
    }
}