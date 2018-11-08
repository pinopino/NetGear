namespace NetGear.Example.Rpc
{
    public class AddParameters
    {
        public long a;
        public long b;
    }

    public interface IDataContract
    {
        long AddMoney(long input1, long input2);
    }
}
