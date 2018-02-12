using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ZLR.VM.IOFilters
{
    public sealed class InternalSaveFilter : FilterBase
    {
        private MemoryStream saveData;

        public InternalSaveFilter([NotNull] IAsyncZMachineIO next)
            : base(next)
        {
        }

        [ItemNotNull]
        public override Task<Stream> OpenSaveFileAsync(int size, CancellationToken cancellationToken = default)
        {
            saveData = new MemoryStream(size);
            return Task.FromResult<Stream>(saveData);
        }

        [ItemNotNull]
        public override Task<Stream> OpenRestoreFileAsync(CancellationToken cancellationToken = default)
        {
            if (saveData != null)
                return Task.FromResult<Stream>(new MemoryStream(saveData.ToArray(), false));

            return Task.FromResult<Stream>(null);
        }
    }
}
