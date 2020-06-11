using System.Threading.Tasks;

namespace NetGear.Core
{
    public interface ITransport
    {
        Task BindAsync();
        Task UnbindAsync();
        Task StopAsync();
    }
}
