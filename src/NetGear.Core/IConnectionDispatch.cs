using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public interface IConnectionDispatcher
    {
        void OnConnection(IDuplexPipe connection);

        Task StopAsync();
    }
}
