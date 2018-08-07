using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Ports;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace SerialDevices
{


    public abstract class IMeasurementDevice
    {
        protected bool _initialized;
        [XmlIgnore]
        public bool Initialized
        {
            get
            {
                return _initialized;
            }
            set
            {
                if (_initialized) _initialized = true;
                else _initialized = value;
            }
        }
        protected string _deviceID;
        public string DeviceID //used to identify device
        {
            get
            {
                return _deviceID;
            }
            set
            {
                if (!Initialized)
                {
                    _deviceID = value;
                }
                else
                {
                    throw new Exception("DeviceID can only be set once");
                }
            }
        }
        public string DeviceName; //used for displaying device
        public List<GenericReadResult> ReadDataTemplate { get; set; }
        [XmlIgnore]
        public DeviceSerial Port { get; set; }


        public delegate double ParseReadValDelegate(string ReadWord);

        public virtual void KickOffRead()
        {
            Port.DiscardInBuffer();
            Port.WriteLine(Port.ReadCommand);
        }

        public virtual double ReadReadyData(ParseReadValDelegate ParseReadVal)
        {
            string message = Port.ReadExisting();
            //make this safe
            return ParseReadVal(message);
        }

        public virtual double ReadVal(ParseReadValDelegate ParseReadVal)
        {
            Port.WriteLine(Port.ReadCommand);
            System.Threading.Thread.Sleep(30);
            string message = Port.ReadLine();
            return ParseReadVal(message);
        }

        public abstract double ParseReadVal_SetAll(string ReadWord);
        public abstract void Start();

        public abstract void SendVal(double Val);

        public IMeasurementDevice()
        {
            ReadDataTemplate = new List<GenericReadResult>();
            Initialized = false;
        }
    }
    [Serializable]
    public class GenericReadResult
    {
        [XmlIgnore]
        public double Result;
        [XmlAttribute]
        public ResultType Type;
        [XmlAttribute]
        public AxisType WhichAxis = AxisType.Primary;

        public GenericReadResult()
        {
            Result = 0;
            Type = ResultType.Other;
        }
        public GenericReadResult(ResultType T)
        {
            Result = 0;
            Type = T;
        }
        public GenericReadResult(double R, ResultType T)
        {
            Result = R;
            Type = T;
        }
    }

    //used to chat with the serial port and find the port
    [Serializable]
    public class DeviceSerial : SerialPort
    {
        private bool foundport;
        public bool FoundPort
        {
            get
            {
                return foundport;
            }
            private set
            {
                foundport = value;
            }
        }

        #region Constructors
        public DeviceSerial() : base() { }
        public DeviceSerial(string Name, string CallString, string ResponseString, string readCommand) : base()
        {
            DeviceName = Name;
            this.CallString = CallString;
            this.ResponseString = ResponseString;
            ReadCommand = readCommand;
            FoundPort = false;
            SetPort();

            //ReadTimeout = 200;
        }
        public DeviceSerial(string deviceName, string callString, string responseString, string readCommand, int baudRate)
        {
            DeviceName = deviceName;
            CallString = callString;
            ResponseString = responseString;
            ReadCommand = readCommand;
            BaudRate = baudRate;
            FoundPort = false;
            SetPort();
            //ReadTimeout = 200;
        }
        public DeviceSerial(string deviceName, string callString, string responseString, string readCommand, int baudRate, Handshake FlowType)
        {
            DeviceName = deviceName;
            CallString = callString;
            ResponseString = responseString;
            ReadCommand = readCommand;
            BaudRate = baudRate;
            Handshake = FlowType;
            FoundPort = false;
            WriteTimeout = 200;
            SecToFindPort = Double.MaxValue;
            SetPort();

            //ReadTimeout = 200;
        }
        #endregion

        #region variables
        public string DeviceName;
        private readonly string CallString;
        private readonly string ResponseString;
        public readonly string ReadCommand;
        private List<string> UsedPorts = new List<string>();
        [XmlIgnore]
        public double SecToFindPort { get; set; }
        #endregion

        public void SetPort()
        {
            double BonusTimeToSearch = 300;
            SearchingForPort:
            string[] checkports = SerialPort.GetPortNames();

            foreach (string checkport in checkports)
            {
                try
                {
                    if (!UsedPorts.Contains(checkport))
                    {
                        SerialPort P = new SerialPort();
                        try
                        {

                            P.PortName = checkport;
                            P.Open();
                            System.Threading.Thread.Sleep(50);
                            P.Close();

                        }
                        catch
                        {
                            UsedPorts.Add(checkport);
                            //P.Close();
                        }
                        PortName = checkport;
                        if (!IsOpen)
                        {
                            Open();
                            this.WriteTimeout = 1000;
                            this.DiscardInBuffer();
                            this.DiscardOutBuffer();
                            this.Write(CallString);

                            System.Threading.Thread.Sleep(50);
                            //MessageBox.Show(this.BytesToRead.ToString());
                            StringBuilder message = new StringBuilder(64);

                            int i = 0;
                            // we'll stop after 500 ms
                            TimeSpan maxDuration = TimeSpan.FromMilliseconds(BonusTimeToSearch + (int)(1000 * (double)ResponseString.Length / ((double)this.BaudRate / (double)this.DataBits)));
                            Stopwatch sw = Stopwatch.StartNew();

                            Stopwatch sw2 = Stopwatch.StartNew();
                            int count = 0;
                            while (sw.Elapsed < maxDuration)
                            {
                                count++;
                                sw2.Reset();
                                sw2.Start();
                                i = BytesToRead;
                                if (Convert.ToBoolean(BytesToRead))
                                {

                                    message.Append(ReadExisting());
                                    string MsgStr = message.ToString();
                                    //MessageBox.Show(MsgStr);
                                    if (ResponseString.StartsWith(MsgStr))
                                    {
                                        if (MsgStr == ResponseString)
                                        {
                                            //MessageBox.Show(UsedPorts.Count.ToString()+" how the fuck you get here");
                                            UsedPorts.Add(this.PortName);
                                            FoundPort = true;
                                            WriteTimeout = InfiniteTimeout;
                                            sw.Stop();
                                            SecToFindPort = Convert.ToDouble(sw.ElapsedMilliseconds) / 1000;
                                            return;
                                        }
                                        else
                                        {

                                        }
                                    }
                                    else
                                    {
                                        this.Close();
                                        break;
                                    }
                                }
                            }
                            this.Close();
                        }
                        else
                        {
                            int i = 1;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e.Message == "The write timed out.")
                    {
                        this.Close();
                    }
                    else
                    {
                        //MessageBox.Show(UsedPorts.Count.ToString()+' '+e.ToString());
                        UsedPorts.Add(this.PortName);
                        try
                        {
                            this.Close();
                        }
                        catch
                        { }
                    }
                }
            }
            DialogResult result = MessageBox.Show(DeviceName + " Port not found.\n Quit?", "Quit?",
                MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
            if (result == DialogResult.Abort)
            {
                FoundPort = false;
                return;
            }
            if (result == DialogResult.Retry)
            {
                BonusTimeToSearch = 600;
                
                goto SearchingForPort;
            }
        }
    }

    [Serializable]
    public enum ResultType
    {
        [XmlEnum("Pressure")]
        Pressure,
        [XmlEnum("Temperature")]
        Temperature,
        [XmlEnum("Flow")]
        Flow,
        [XmlEnum("Resistance")]
        Resistance,
        [XmlEnum("Frequency")]
        Frequency,
        [XmlEnum("Current")]
        Current,
        [XmlEnum("Value")]
        Value,
        [XmlEnum("Valve")]
        Valve,
        [XmlEnum("Other")]
        Other
    }
}
