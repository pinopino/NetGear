using System.Threading.Tasks;

namespace NetGear.Core
{
    public interface IAsyncDisposable
    {
        Task DisposeAsync();
    }
}
