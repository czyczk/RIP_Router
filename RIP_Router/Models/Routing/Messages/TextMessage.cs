using System;
using System.Collections.Generic;
using System.Text;

namespace RIP_Router.Models.Routing.Messages
{
    public class TextMessage
    {
        public uint SourceRouterId { get; }
        public uint DestinationRouterId { get; }
        public string Message { get; }

        public TextMessage(uint sourceRouterId, uint destinationRouterId, string message)
        {
            SourceRouterId = sourceRouterId;
            DestinationRouterId = destinationRouterId;
            Message = message;
        }
    }
}
