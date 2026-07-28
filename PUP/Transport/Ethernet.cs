/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using SharpPcap;
using IFS.Logging;
using System.IO;
using System.Threading;
using IFS.Gateway;
using System.Net.NetworkInformation;
using PacketDotNet;

namespace IFS.Transport
{
    /// <summary>
    /// Defines interface "to the metal" (raw ethernet frames) using WinPCAP to send and receive Ethernet
    /// frames.
    /// 
    /// Ethernet packets are broadcast.  See comments in UDP.cs for the reasoning behind this.
    /// 
    /// </summary>
    public class Ethernet : IPacketInterface
    {
        public Ethernet(ILiveDevice iface)
        {
            _interface = iface;
        }

        public void RegisterRouterCallback(ReceivedPacketCallback callback)
        {
            _routerCallback = callback;

            // Now that we have a callback we can start receiving stuff.
            Open(false /* not promiscuous */, 0);

            // Kick off the receiver thread, this will never return or exit.
            Thread receiveThread = new Thread(new ThreadStart(BeginReceive));
            receiveThread.Start();
        }

        public void Shutdown()
        {
            _routerCallback = null;
            _interface.Close();
        }
        
        public void Send(PUP p)
        {
            //
            // Write PUP to ethernet:
            //
            byte[] encapsulatedFrame = PupPacketBuilder.BuildEncapsulatedEthernetFrameFromPup(p);
            SendFrame(encapsulatedFrame);
        }

        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            byte[] encapsulatedFrame = PupPacketBuilder.BuildEncapsulatedEthernetFrameFromRawData(data, source, destination, frameType);
            SendFrame(encapsulatedFrame);
        }

        public void Send(MemoryStream encapsulatedFrameStream)
        {
            SendFrame(encapsulatedFrameStream.ToArray());
        }

        private void SendFrame(byte[] encapsulatedFrame)
        {

            EthernetPacket p = new EthernetPacket(
                _interface.MacAddress,      // Source address
                _10mbitBroadcast,           // Destnation (broadcast)
                (EthernetType)_3mbitFrameType);

            p.PayloadData = encapsulatedFrame;
            _interface.SendPacket(p);
        }

        private void ReceiveCallback(object sender, PacketCapture e)
        { 
            if (e.GetPacket().LinkLayerType != LinkLayers.Ethernet)
            {
                return;
            }

            
            try
            {
                EthernetPacket packet = (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, e.GetPacket().Data);
                if ((int)packet.Type == _3mbitFrameType)
                {
                    Log.Write(LogType.Verbose, LogComponent.Ethernet, "3mbit pup received.");

                    MemoryStream packetStream = new MemoryStream(packet.PayloadData);
                    _routerCallback(packetStream, this);
                }
                else
                {
                    // Not an encapsulated 3mbit frame, Discard the packet.  We will not log this, so as to keep noise down. 
                    // Log.Write(LogType.Verbose, LogComponent.Ethernet, "Not a PUP (type 0x{0:x}.  Dropping.", p.Ethernet.EtherType);
                }
            }
            catch (Exception ex)
            {
                Log.Write(LogType.Error, LogComponent.Ethernet, "Internal error: failed to dispatch packet.  Exception:\n {0}", ex.ToString());
            }
        }

        private void Open(bool promiscuous, int timeout)
        {
            DeviceConfiguration config = new DeviceConfiguration();
            config.Mode = promiscuous ? DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness : DeviceModes.MaxResponsiveness;
            config.ReadTimeout = timeout;
            config.Immediate = true;
            _interface.Open(config);
        }

        /// <summary>
        /// Begin receiving packets, forever.
        /// </summary>
        private void BeginReceive()
        {
            _interface.OnPacketArrival += ReceiveCallback;
            _interface.StartCapture();
        }        

        private ILiveDevice _interface;
        private ReceivedPacketCallback _routerCallback;

        // Constants

        // The type used for 3mbit frames encapsulated in 10mb frames
        private readonly int _3mbitFrameType = 0xbeef;     // easy to identify, ostensibly unused by anything of any import        

        // 10mbit broadcast address
        private PhysicalAddress _10mbitBroadcast = new PhysicalAddress(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff });

    }
}
