using System;
using JetBrains.Annotations;

namespace ZLR.VM.IOFilters
{
    public abstract class FilterBase : IZMachineIO
    {
        protected readonly IZMachineIO next;

        protected FilterBase([NotNull] IZMachineIO next)
        {
            this.next = next ?? throw new ArgumentNullException(nameof(next));
        }

        #region IZMachineIO Members

        public virtual ReadLineResult ReadLine(string initial, int time, TimedInputCallback callback, byte[] terminatingKeys,
            bool allowDebuggerBreak) => next.ReadLine(initial, time, callback, terminatingKeys, allowDebuggerBreak);

        public virtual short ReadKey(int time, TimedInputCallback callback, CharTranslator translator) =>
            next.ReadKey(time, callback, translator);

        public virtual void PutCommand(string command) => next.PutCommand(command);

        public virtual void PutChar(char ch) => next.PutChar(ch);

        public virtual void PutString(string str) => next.PutString(str);

        public virtual void PutTextRectangle(string[] lines) => next.PutTextRectangle(lines);

        public virtual bool Buffering
        {
            get => next.Buffering;
            set => next.Buffering = value;
        }

        public virtual bool Transcripting
        {
            get => next.Transcripting;
            set => next.Transcripting = value;
        }

        public virtual void PutTranscriptChar(char ch) => next.PutTranscriptChar(ch);

        public virtual void PutTranscriptString(string str) => next.PutTranscriptString(str);

        public virtual System.IO.Stream OpenSaveFile(int size) => next.OpenSaveFile(size);

        public virtual System.IO.Stream OpenRestoreFile() => next.OpenRestoreFile();

        public virtual System.IO.Stream OpenAuxiliaryFile(string name, int size, bool writing) =>
            next.OpenAuxiliaryFile(name, size, writing);

        public virtual System.IO.Stream OpenCommandFile(bool writing) => next.OpenCommandFile(writing);

        public virtual void SetTextStyle(TextStyle style) => next.SetTextStyle(style);

        public virtual void SplitWindow(short lines) => next.SplitWindow(lines);

        public virtual void SelectWindow(short num) => next.SelectWindow(num);

        public virtual void EraseWindow(short num) => next.EraseWindow(num);

        public virtual void EraseLine() => next.EraseLine();

        public virtual void MoveCursor(short x, short y) => next.MoveCursor(x, y);

        public virtual void GetCursorPos(out short x, out short y) => next.GetCursorPos(out x, out y);

        public virtual void SetColors(short fg, short bg) => next.SetColors(fg, bg);

        public virtual short SetFont(short num) => next.SetFont(num);

        public virtual bool DrawCustomStatusLine(string location, short hoursOrScore, short minsOrTurns,
            bool useTime) =>
            next.DrawCustomStatusLine(location, hoursOrScore, minsOrTurns, useTime);

        public virtual void PlaySoundSample(ushort number, SoundAction action, byte volume, byte repeats,
            SoundFinishedCallback callback) =>
            next.PlaySoundSample(number, action, volume, repeats, callback);

        public virtual void PlayBeep(bool highPitch) => next.PlayBeep(highPitch);

        public virtual bool ForceFixedPitch
        {
            get => next.ForceFixedPitch;
            set => next.ForceFixedPitch = value;
        }

        public virtual bool VariablePitchAvailable => next.VariablePitchAvailable;

        public virtual bool ScrollFromBottom
        {
            get => next.ScrollFromBottom;
            set => next.ScrollFromBottom = value;
        }

        public virtual bool BoldAvailable => next.BoldAvailable;

        public virtual bool ItalicAvailable => next.ItalicAvailable;

        public virtual bool FixedPitchAvailable => next.FixedPitchAvailable;

        public virtual bool GraphicsFontAvailable => next.GraphicsFontAvailable;

        public virtual bool TimedInputAvailable => next.TimedInputAvailable;

        public virtual bool SoundSamplesAvailable => next.SoundSamplesAvailable;

        public virtual byte WidthChars => next.WidthChars;

        public virtual short WidthUnits => next.WidthUnits;

        public virtual byte HeightChars => next.HeightChars;

        public virtual short HeightUnits => next.HeightUnits;

        public virtual byte FontHeight => next.FontHeight;

        public virtual byte FontWidth => next.FontWidth;

        public virtual event EventHandler SizeChanged
        {
            add => next.SizeChanged += value;
            remove => next.SizeChanged -= value;
        }

        public virtual bool ColorsAvailable => next.ColorsAvailable;

        public virtual byte DefaultForeground => next.DefaultForeground;

        public virtual byte DefaultBackground => next.DefaultBackground;

        public virtual UnicodeCaps CheckUnicode(char ch) => next.CheckUnicode(ch);

        #endregion
    }
}