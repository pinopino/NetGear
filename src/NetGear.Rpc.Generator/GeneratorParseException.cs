using System;

namespace NetGear.Rpc.Generator
{
    public class GeneratorParseException : ApplicationException
    {
        public GeneratorParseException(string message)
            : base(message)
        { }

        public override string Message
        {
            get
            {
                return base.Message;
            }
        }
    }
}
