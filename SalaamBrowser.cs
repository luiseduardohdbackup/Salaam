using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;

namespace Dolphins.Salaam
{
    /// <summary>
    /// This class is used to find the SalaamService servers.
    /// </summary>
    public class SalaamBrowser:IDisposable
    {

        private List<SalaamClient> clientsList;

        private const int port = 54143;

        private const int disappearanceDelaySeconds = 4;

        private UdpClient udpClient;

        private int delay;

        private TimeSpan disappearanceDelay;

        private Timer timer;

        private readonly IPEndPoint ipEndPoint;

        private string currentHostName;

        private readonly List<IPAddress> currentIPAddresses;

        private string selfServiceType;

        private int selfServicePort;

        private bool isBrowserRunning;

        /// <summary>
        /// Gets a value indicating whether the browsers receives self packets or not.
        /// </summary>
        /// <value><c>true</c> if the browser receives self packets; otherwise, <c>false</c>.</value>
        public bool ReceivesSelfPackets { get; private set; }

        public delegate void SalaamClientEventHandler(object sender, SalaamClientEventArgs e);

        /// <summary>
        /// Occurs when a new client appears.
        /// </summary>
        /// <remarks></remarks>
        public event SalaamClientEventHandler ClientAppeared;

        /// <summary>
        /// Occurs when a found client disappears.
        /// </summary>
        /// <remarks></remarks>
        public event SalaamClientEventHandler ClientDisappeared;

        /// <summary>
        /// Occurs when the client message changes.
        /// </summary>
        /// <remarks></remarks>
        public event SalaamClientEventHandler ClientMessageChanged;

        /// <summary>
        /// Occurs when the browser starts listening for incoming connections.
        /// </summary>
        public event EventHandler Started;

        /// <summary>
        /// Occurs when browsers stops listening for incoming connections.
        /// </summary>
        public event EventHandler Stopped;

        /// <summary>
        /// Occurs when the browser fails to start.
        /// </summary>
        public event EventHandler StartFailed;

        /// <summary>
        /// Occurs when something happens and the browser cannot continue listening for incoming connections anymore.
        /// </summary>
        public event EventHandler BrowserFailed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SalaamBrowser"/> class.
        /// </summary>
        public SalaamBrowser()
        {
            timer = new Timer();
            
            DisappearanceDelay = disappearanceDelaySeconds;

            timer.Elapsed += new ElapsedEventHandler(OnTimerElapsed);

            timer.Start();

            timer.Enabled = false;

            ipEndPoint = new IPEndPoint(IPAddress.Any, port);

            currentIPAddresses = new List<IPAddress>();

            clientsList = new List<SalaamClient>();
        }

        /// <summary>
        /// Sets the self packet receive.
        /// </summary>
        /// <param name="serviceType">Type of the service.</param>
        /// <param name="servicePort">The application port.</param>
        /// <param name="receiveSelfPackets">if set to <c>true</c> handles when the application appers on the local host.</param>
        public void SetSelfPacketReceive(string serviceType, int servicePort, bool receiveSelfPackets)
        {
            selfServiceType = serviceType;

            selfServicePort = servicePort;

            ReceivesSelfPackets = receiveSelfPackets;
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            List<SalaamClient> salaamClients = new List<SalaamClient>();

            for (int i = 0; i < clientsList.Count; i++)
            {
                if (DateTime.Now.Subtract((DateTime)clientsList[i].LastTimeSeen) > disappearanceDelay)
                {
                    if (ClientDisappeared != null)
                    {
                        ClientDisappeared(this, new SalaamClientEventArgs(clientsList[i]));
                    }
                }
                else
                {
                    salaamClients.Add(clientsList[i]);
                }
            }

            clientsList = salaamClients;
        }

        ~SalaamBrowser()
        {
            Dispose();
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="SalaamBrowser"/> is enabled.
        /// </summary>
        /// <value><c>true</c> if enabled; otherwise, <c>false</c>.</value>
        public bool Enabled
        {
            get { return timer.Enabled; }
            set
            {
                timer.Enabled = value;

                if (value == true)
                {
                    Start(ServiceType);
                }
                else
                {
                    Stop();
                }
            }
        }

        /// <summary>
        /// Gets or sets the disappearance delay in seconds.
        /// </summary>
        /// <value>The disappearance delay.</value>
        /// <remarks></remarks>
        public int DisappearanceDelay
        {
            get
            {
                return delay;
            }
            set
            {
                delay = value;

                timer.Interval = 1000 * ((double)delay/5);

                disappearanceDelay = new TimeSpan(0, 0, DisappearanceDelay);
            }
        }

        /// <summary>
        /// Gets the type of the service.
        /// </summary>
        public string ServiceType { get; private set; }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            try
            {
                timer.Stop();
            }
            catch
            {
            }

            try
            {
                timer.Dispose();
            }
            catch
            {
            }

            try
            {
                timer = null;
            }
            catch
            {
            }

            try
            {
                udpClient.Client.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            try
            {
                udpClient.Close();
            }
            catch
            {
            }

            try
            {
                udpClient = null;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Starts the specified service type.
        /// </summary>
        /// <param name="serviceType">Type of the service.</param>
        public void Start(string serviceType)
        {
            if (serviceType.Contains(";"))
            {
                throw new ArgumentException("Semicolon character is not allowed in ServiceType argument.");
            }

            try
            {
                currentHostName = Dns.GetHostName();

                currentIPAddresses.Clear();

                currentIPAddresses.AddRange(Dns.GetHostAddresses(currentHostName));

                ServiceType = serviceType;

                timer.Enabled = true;

                udpClient = new UdpClient { EnableBroadcast = true, ExclusiveAddressUse = true };

                udpClient.Client.Bind(ipEndPoint);

                udpClient.BeginReceive(OnUdpDataReceived, udpClient);

                if (Started != null)
                {
                    Started(this, new EventArgs());
                }

                isBrowserRunning = true;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                if (StartFailed != null)
                {
                    StartFailed(this, new EventArgs());
                }

                try
                {
                    timer.Enabled = false;
                }
                catch
                {
                }
            }
        }

        private void OnUdpDataReceived(IAsyncResult asyncResult)
        {
            IPEndPoint tempIPEndPoint = new IPEndPoint(ipEndPoint.Address, ipEndPoint.Port);

            UdpClient tempUdpClient = (UdpClient) asyncResult.AsyncState;

            try
            {
                byte[] bytes = tempUdpClient.EndReceive(asyncResult, ref tempIPEndPoint);

                ProcessClientData(bytes, tempIPEndPoint);
            }
            catch(ObjectDisposedException)
            {
            }

            try
            {
                udpClient.BeginReceive(OnUdpDataReceived, udpClient);
            }
            catch
            {
                if (BrowserFailed != null && isBrowserRunning)
                {
                    BrowserFailed(this, new EventArgs());
                }
            }
        }

        private void ProcessClientData(byte[] dataBytes, IPEndPoint clientIPEndPoint)
        {
            const string messageRegex = "^Salaam:(?<Base64>(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?)$";

            const string messageDataRegex =
                @"^(?<Length>\d+);(?<HostName>.*?);(?<ServiceType>.*?);(?<Name>.*?);(?<Port>\d+);(?<Message>.*);(<(?<ProtocolMessage>[A-Z][A-Z0-9]{2,3})>)?$";

            string dataString = Encoding.UTF8.GetString(dataBytes);

            if (Regex.IsMatch(dataString, messageRegex))
            {
                try
                {
                    string messageData =
                        Encoding.UTF8.GetString(
                            Convert.FromBase64String(Regex.Match(dataString, messageRegex).Groups["Base64"].Value));

                    if (Regex.IsMatch(messageData, messageDataRegex))
                    {
                        try
                        {
                            Match dataMatch = Regex.Match(messageData,messageDataRegex);

                            int length = int.Parse(dataMatch.Groups["Length"].Value);

                            length += length.ToString().Length + 1;

                            if (messageData.Length == length)
                            {
                                string hostName = dataMatch.Groups["HostName"].Value;

                                string serviceType = dataMatch.Groups["ServiceType"].Value;

                                string name = dataMatch.Groups["Name"].Value;

                                int port = int.Parse(dataMatch.Groups["Port"].Value);

                                string message = dataMatch.Groups["Message"].Value;

                                IPAddress ipAddress = clientIPEndPoint.Address;

                                string protocolMessage = "";

                                if (dataMatch.Groups["ProtocolMessage"].Success)
                                {
                                    protocolMessage = dataMatch.Groups["ProtocolMessage"].Value;
                                }

                                if (!ReceivesSelfPackets)
                                {
                                    if (hostName.ToLower() == currentHostName.ToLower() &&
                                        currentIPAddresses.Contains(ipAddress) &&
                                        (serviceType.ToLower() == selfServiceType.ToLower()) &&
                                        port == selfServicePort)
                                    {
                                        return;
                                    }
                                }

                                if (ServiceType == "*" || serviceType.ToLower() == ServiceType)
                                {
                                    SalaamClient salaamClient = new SalaamClient(ipAddress, hostName, serviceType, name,
                                                                                 port, message);

                                    processSalaamClient(salaamClient, protocolMessage);
                                }
                                else
                                {
                                    return;
                                }
                            }
                            else
                            {
                                return;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private void processSalaamClient(SalaamClient salaamClient, string protocolMessage)
        {
            bool contains = clientsList.Contains(salaamClient);

            if (contains)
            {
                int index = clientsList.IndexOf(salaamClient);

                clientsList[index].LastTimeSeen = DateTime.Now;

                if (string.IsNullOrEmpty(protocolMessage))
                {
                    if (salaamClient.Message != clientsList[index].Message)
                    {
                        clientsList[index].SetMessage(salaamClient.Message);

                        if (ClientMessageChanged != null)
                        {
                            ClientMessageChanged(this, new SalaamClientEventArgs(clientsList[index]));
                        }
                    }
                }
                else
                {
                    if (protocolMessage.ToUpper() == "EOS")
                    {
                        if (ClientDisappeared != null)
                        {
                            ClientDisappeared(this, new SalaamClientEventArgs(clientsList[clientsList.IndexOf(salaamClient)]));

                            clientsList.Remove(salaamClient);
                        }
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(protocolMessage))
                {
                    clientsList.Add(salaamClient);

                    int index = clientsList.Count - 1;

                    clientsList[index].LastTimeSeen = DateTime.Now;

                    if (ClientAppeared != null)
                    {
                        ClientAppeared(this, new SalaamClientEventArgs(clientsList[index]));
                    }
                }
                else
                {
                    if (protocolMessage.ToUpper() == "EOS")
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Stops the service.
        /// </summary>
        public void Stop()
        {
            isBrowserRunning = false;

            try
            {
                timer.Enabled = false;
            }
            catch
            {
            }

            try
            {
                udpClient.Client.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            try
            {
                udpClient.Close();
            }
            catch
            {
            }

            if (Stopped != null)
            {
                Stopped(this, new EventArgs());
            }
        }
    }
}
