namespace RIP_Router.Models.Routing.Messages
{
    public class RouterIdentity
    {
        public uint RouterId { get; }

        public RouterIdentity(uint routerId)
        {
            RouterId = routerId;
        }
    }
}
