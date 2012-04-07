using System;

namespace Dolphins.Salaam
{
    public class SalaamClientEventArgs:EventArgs
    {
        public SalaamClientEventArgs(SalaamClient client)
        {
            Client = client;
        }

        public SalaamClient Client { get; private set; }
    }
}
