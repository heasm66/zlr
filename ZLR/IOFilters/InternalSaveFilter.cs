using System.IO;
using JetBrains.Annotations;

namespace ZLR.VM.IOFilters
{
    public sealed class InternalSaveFilter : FilterBase
    {
        private MemoryStream saveData;

        public InternalSaveFilter([NotNull] IZMachineIO next)
            : base(next)
        {
        }

        [NotNull]
        public override Stream OpenSaveFile(int size)
        {
            saveData = new MemoryStream(size);
            return saveData;
        }

        public override Stream OpenRestoreFile()
        {
            if (saveData != null)
                return new MemoryStream(saveData.ToArray(), false);

            return null;
        }
    }
}
