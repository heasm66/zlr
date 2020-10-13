using System;
using System.IO;
using System.Text;

namespace ZLR.VM.Tests
{
    abstract class TestCaseIO : IZMachineIO
    {
        protected readonly StringBuilder outputBuffer = new StringBuilder();
        protected MemoryStream? saveData;

        public string CollectOutput()
        {
            var result = outputBuffer.ToString();
            outputBuffer.Length = 0;
            return result;
        }

        #region IZMachineIO Members

        public abstract ReadLineResult ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, bool allowDebuggerBreak);

        public abstract short ReadKey(int time, TimedInputCallback callback, CharTranslator translator);

        public void PutCommand(string command)
        {
            // nada
        }

        public abstract void PutChar(char ch);

        public abstract void PutString(string str);

        public abstract void PutTextRectangle(string[] lines);

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
            // nada
        }

        public void PutTranscriptString(string str)
        {
            // nada
        }

        public Stream OpenSaveFile(int size)
        {
            saveData = new MemoryStream();
            return saveData;
        }

        public Stream? OpenRestoreFile()
        {
            return saveData != null ? new MemoryStream(saveData.ToArray(), false) : null;
        }

        public Stream? OpenAuxiliaryFile(string name, int size, bool writing)
        {
            return null;
        }

        public abstract Stream? OpenCommandFile(bool writing);

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
            // nada
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
            // not supported
            return 0;
        }

        public bool DrawCustomStatusLine(string location, short hoursOrScore, short minsOrTurns, bool useTime)
        {
            return false;
        }

        public void PlaySoundSample(ushort number, SoundAction action, byte volume, byte repeats, SoundFinishedCallback callback)
        {
            // nada
        }

        public virtual void PlayBeep(bool highPitch)
        {
            // nada
        }

        public bool ForceFixedPitch
        {
            get => true;
            set { /* nada */ }
        }

        public bool VariablePitchAvailable => false;

        public bool ScrollFromBottom
        {
            get => false;
            set { /* nada */ }
        }

        public bool BoldAvailable => false;

        public bool ItalicAvailable => false;

        public bool FixedPitchAvailable => false;

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
            return UnicodeCaps.CanPrint | UnicodeCaps.CanInput;
        }

        #endregion
    }

    class ReplayIO : TestCaseIO
    {
        private readonly string inputFile;

        public ReplayIO(string prevInputFile)
        {
            inputFile = prevInputFile;
        }

        public override ReadLineResult ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, bool allowDebuggerBreak)
        {
            // if we get here, the command file has been exhausted
            throw new InvalidOperationException("No more test case input");
        }

        public override short ReadKey(int time, TimedInputCallback callback, CharTranslator translator)
        {
            // if we get here, the command file has been exhausted
            throw new InvalidOperationException("No more test case input");
        }

        public override void PutChar(char ch)
        {
            outputBuffer.Append(ch);
        }

        public override void PutString(string str)
        {
            outputBuffer.Append(str);
        }

        public override void PutTextRectangle(string[] lines)
        {
            foreach (var line in lines)
                outputBuffer.AppendLine(line);
        }

        public override Stream? OpenCommandFile(bool writing)
        {
            return writing ? null : new FileStream(inputFile, FileMode.Open, FileAccess.Read);
        }
    }

    class RecordingIO : TestCaseIO
    {
        private readonly string inputFile;

        public RecordingIO(string newInputFile)
        {
            inputFile = newInputFile;
        }

        public override ReadLineResult ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, bool allowDebuggerBreak)
        {
            var line = Console.ReadLine();
            return line == null ? ReadLineResult.Cancelled : ReadLineResult.LineEntered(line);
        }

        public override short ReadKey(int time, TimedInputCallback callback, CharTranslator translator)
        {
            var info = Console.ReadKey(true);
            return translator(info.KeyChar);
        }

        public override void PutChar(char ch)
        {
            Console.Write(ch);
            outputBuffer.Append(ch);
        }

        public override void PutString(string str)
        {
            Console.Write(str);
            outputBuffer.Append(str);
        }

        public override void PutTextRectangle(string[] lines)
        {
            foreach (var str in lines)
            {
                Console.WriteLine(str);
                outputBuffer.AppendLine(str);
            }
        }

        public override Stream? OpenCommandFile(bool writing)
        {
            return writing ? new FileStream(inputFile, FileMode.Create, FileAccess.Write) : null;
        }

        public override void PlayBeep(bool highPitch)
        {
            Console.Beep(highPitch ? 1600 : 800, 200);
        }
    }
}
