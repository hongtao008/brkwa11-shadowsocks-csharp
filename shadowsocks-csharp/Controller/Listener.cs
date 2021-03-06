﻿using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Controller
{
    public class Listener
    {
        public interface Service
        {
            bool Handle(byte[] firstPacket, int length, Socket socket);
        }

        Configuration _config;
        bool _shareOverLAN;
        Socket _socket;
        Socket _socket_v6;
        IList<Service> _services;

        public Listener(IList<Service> services)
        {
            this._services = services;
        }

        private bool CheckIfPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();

            foreach (IPEndPoint endPoint in ipEndPoints)
            {
                if (endPoint.Port == port)
                {
                    return true;
                }
            }
            return false;
        }

        public void Start(Configuration config)
        {
            this._config = config;
            this._shareOverLAN = config.shareOverLan;

            if (CheckIfPortInUse(_config.localPort))
                throw new Exception(I18N.GetString("Port already in use"));

            try
            {
                // Create a TCP/IP socket.
                bool ipv6 = true;
                //bool ipv6 = false;
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (ipv6)
                {
                    try
                    {
                        _socket_v6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                        _socket_v6.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    }
                    catch
                    {
                        _socket_v6 = null;
                    }
                }
                IPEndPoint localEndPoint = null;
                IPEndPoint localEndPointV6 = null;
                if (_shareOverLAN)
                {
                    localEndPoint = new IPEndPoint(IPAddress.Any, _config.localPort);
                    localEndPointV6 = new IPEndPoint(IPAddress.IPv6Any, _config.localPort);
                }
                else
                {
                    localEndPoint = new IPEndPoint(IPAddress.Loopback, _config.localPort);
                    localEndPointV6 = new IPEndPoint(IPAddress.IPv6Loopback, _config.localPort);
                }

                // Bind the socket to the local endpoint and listen for incoming connections.
                _socket.Bind(localEndPoint);
                _socket.Listen(1024);
                if (_socket_v6 != null)
                {
                    _socket_v6.Bind(localEndPointV6);
                    _socket_v6.Listen(1024);
                }


                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("Shadowsocks started");
                _socket.BeginAccept(
                    new AsyncCallback(AcceptCallback),
                    _socket);
                if (_socket_v6 != null)
                    _socket_v6.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        _socket_v6);
            }
            catch (SocketException)
            {
                _socket.Close();
                if (_socket_v6 != null)
                    _socket_v6.Close();
                throw;
            }
        }

        public void Stop()
        {
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            if (_socket_v6 != null)
            {
                _socket_v6.Close();
                _socket_v6 = null;
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            Socket listener = (Socket)ar.AsyncState;
            try
            {
                Socket conn = listener.EndAccept(ar);

                byte[] buf = new byte[4096];
                object[] state = new object[] {
                    conn,
                    buf
                };

                conn.BeginReceive(buf, 0, buf.Length, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                try
                {
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
        }


        private void ReceiveCallback(IAsyncResult ar)
        {
            object[] state = (object[])ar.AsyncState;

            Socket conn = (Socket)state[0];
            byte[] buf = (byte[])state[1];
            try
            {
                int bytesRead = conn.EndReceive(ar);
                foreach (Service service in _services)
                {
                    if (service.Handle(buf, bytesRead, conn))
                    {
                        return;
                    }
                }
                // no service found for this
                // shouldn't happen
                conn.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                conn.Close();
            }
        }
    }
}
