using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ZLR.VM;

namespace ZLR.Interfaces.SystemConsole
{
    [SuppressMessage("ReSharper", "LocalizableElement")]
    class DumbIO : IZMachineIO
    {
        private readonly bool bottomWinOnly;
        private string? suppliedCommandFile;
        private short curWin;

        public DumbIO(bool bottomWinOnly, string? commandFile)
        {
            this.bottomWinOnly = bottomWinOnly;
            suppliedCommandFile = commandFile;
        }

        public ReadLineResult ReadLine(string initial, int time, TimedInputCallback callback,
            byte[] terminatingKeys, bool allowDebuggerBreak)
        {
            var text = Console.ReadLine() ?? "";

            if (allowDebuggerBreak &&
                (text.Equals("/break", StringComparison.CurrentCultureIgnoreCase) ||
                 text.Equals("/b", StringComparison.CurrentCultureIgnoreCase)))
            {
                return ReadLineResult.DebuggerBreak;
            }

            return ReadLineResult.LineEntered(text);
        }

        public short ReadKey(int time, TimedInputCallback callback, CharTranslator translator)
        {
            short ch;
            do
            {
                var info = Console.ReadKey();
                ch = translator(info.KeyChar);
            } while (ch == 0);
            return ch;
        }

        public void PutCommand(string command)
        {
            // nada
        }

        public void PutChar(char ch)
        {
            if (!bottomWinOnly || curWin == 0)
                Console.Write(ch);
        }

        public void PutString(string str)
        {
            if (!bottomWinOnly || curWin == 0) 
                Console.Write(str);
        }

        public void PutTextRectangle(string[] lines)
        {
            if (!bottomWinOnly || curWin == 0) 
                foreach (var str in lines)
                    Console.WriteLine(str);
        }

        public bool Buffering
        {
            get => false;
            set { /* nada */ }
        }

        public bool Transcripting
        {
            get => false;
            set { /* nada */ }
        }

        public void PutTranscriptChar(char ch)
        {
            // not implemented
        }

        public void PutTranscriptString(string str)
        {
            // not implemented
        }

        public Stream? OpenSaveFile(int size)
        {
            // not implemented
            return null;
        }

        public Stream? OpenRestoreFile()
        {
            // not implemented
            return null;
        }

        public Stream? OpenAuxiliaryFile(string name, int size, bool writing)
        {
            // not implemented
            return null;
        }

        public Stream? OpenCommandFile(bool writing)
        {
            string filename;
            if (suppliedCommandFile != null)
            {
                filename = suppliedCommandFile;
                suppliedCommandFile = null;
            }
            else
            {
                do
                {
                    Console.Write("Enter the name of a command file to {0} (blank to cancel): ",
                        writing ? "record" : "play back");
                    filename = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(filename))
                        return null;

                    if (writing)
                    {
                        // if the file exists, prompt to overwrite it
                        if (File.Exists(filename))
                        {
                            if (YesOrNoPrompt($"\"{filename}\" exists. Are you sure (y/n)? "))
                                break;
                        }
                        else
                            break;
                    }
                    else
                    {
                        // the file must already exist
                        if (File.Exists(filename))
                            break;
                    }
                }
                while (true);
            }

            return new FileStream(filename,
                    writing ? FileMode.Create : FileMode.Open,
                    writing ? FileAccess.Write : FileAccess.Read);
        }

        private static bool YesOrNoPrompt(string prompt)
        {
            string yorn;
            do
            {
                Console.Write(prompt);
                yorn = Console.ReadLine()?.ToLower().Trim() ?? "n";
            } while (yorn.Length == 0);

            return yorn[0] == 'y';
        }

        public void SetTextStyle(TextStyle style)
        {
            // nada
        }

        public void SplitWindow(short lines)
        {
            // nada
        }

        public void SelectWindow(short num)
        {
            curWin = num;
        }

        public void EraseWindow(short num)
        {
            // nada
        }

        public void EraseLine()
        {
            // nada
        }

        public void MoveCursor(short x, short y)
        {
            // nada
        }

        public void GetCursorPos(out short x, out short y)
        {
            x = 1;
            y = 1;
        }

        public void SetColors(short fg, short bg)
        {
            // nada
        }

        public short SetFont(short num)
        {
            return 0;
        }

        public void PlaySoundSample(ushort number, SoundAction action, byte volume, byte repeats, SoundFinishedCallback callback)
        {
            // nada
        }

        public void PlayBeep(bool highPitch)
        {
            // nada
        }

        public bool ForceFixedPitch
        {
            get => false;
            set { /* nada */ }
        }

        public bool BoldAvailable => false;

        public bool ItalicAvailable => false;

        public bool FixedPitchAvailable => false;

        public bool VariablePitchAvailable => false;

        public bool ScrollFromBottom
        {
            get => false;
            set { /* nada */ }
        }

        public bool GraphicsFontAvailable => false;

        public bool TimedInputAvailable => false;

        public bool SoundSamplesAvailable => false;

        public byte WidthChars => 80;

        public short WidthUnits => 80;

        public byte HeightChars => 25;

        public short HeightUnits => 25;

        public byte FontHeight => 1;

        public byte FontWidth => 1;

        public event EventHandler SizeChanged
        {
            add { /* nada */ }
            remove { /* nada */ }
        }

        public bool ColorsAvailable => false;

        public byte DefaultForeground => 9;

        public byte DefaultBackground => 2;

        public UnicodeCaps CheckUnicode(char ch)
        {
            return UnicodeCaps.CanInput | UnicodeCaps.CanPrint;
        }

        public bool DrawCustomStatusLine(string location, short hoursOrScore, short minsOrTurns, bool useTime)
        {
            return false;
        }
    }
}
