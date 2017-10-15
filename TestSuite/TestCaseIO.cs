using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using ZLR.VM;

namespace TestSuite
{
    abstract class TestCaseIO : IZMachineIO
    {
        protected readonly Queue<string> inputBuffer = new Queue<string>(); 
        protected readonly StringBuilder outputBuffer = new StringBuilder();
        protected MemoryStream saveData;

        [NotNull]
        public string CollectOutput()
        {
            string result = outputBuffer.ToString();
            outputBuffer.Length = 0;
            return result;
        }

        #region IZMachineIO Members

        public abstract string ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, out byte terminator);

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
            get { return false; }
            set { /* nada */ }
        }

        public bool Transcripting
        {
            get { return false; }
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

        [NotNull]
        public Stream OpenSaveFile(int size)
        {
            saveData = new MemoryStream();
            return saveData;
        }

        public Stream OpenRestoreFile()
        {
            if (saveData != null)
                return new MemoryStream(saveData.ToArray(), false);

            return null;
        }

        public Stream OpenAuxiliaryFile(string name, int size, bool writing)
        {
            return null;
        }

        public abstract Stream OpenCommandFile(bool writing);

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
            get { return true; }
            set { /* nada */ }
        }

        public bool VariablePitchAvailable => false;

        public bool ScrollFromBottom
        {
            get { return false; }
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

        public override string ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, out byte terminator)
        {
            terminator = 13;
            return inputBuffer.Dequeue();
        }

        public override short ReadKey(int time, TimedInputCallback callback, CharTranslator translator)
        {
            string inputLine;
            do { inputLine = inputBuffer.Dequeue(); } while (inputLine.Length == 0);
            return translator(inputLine[0]);
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
            foreach (string line in lines)
                outputBuffer.AppendLine(line);
        }

        public override Stream OpenCommandFile(bool writing)
        {
            if (writing)
                return null;

            return new FileStream(inputFile, FileMode.Open, FileAccess.Read);
        }
    }

    class RecordingIO : TestCaseIO
    {
        private readonly string inputFile;

        public RecordingIO(string newInputFile)
        {
            inputFile = newInputFile;
        }

        public override string ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys, out byte terminator)
        {
            terminator = 13;
            return Console.ReadLine() ?? string.Empty;
        }

        public override short ReadKey(int time, TimedInputCallback callback, CharTranslator translator)
        {
            ConsoleKeyInfo info = Console.ReadKey(true);
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
            foreach (string str in lines)
            {
                Console.WriteLine(str);
                outputBuffer.AppendLine(str);
            }
        }

        public override Stream OpenCommandFile(bool writing)
        {
            if (!writing)
                return null;

            return new FileStream(inputFile, FileMode.Create, FileAccess.Write);
        }

        public override void PlayBeep(bool highPitch)
        {
            Console.Beep(highPitch ? 1600 : 800, 200);
        }
    }
}
