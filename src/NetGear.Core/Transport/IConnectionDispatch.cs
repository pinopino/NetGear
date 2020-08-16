using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public interface IConnectionDispatcher
    {
        Task OnConnection(TransportConnection connection);

        Task StopAsync();
    }
}
