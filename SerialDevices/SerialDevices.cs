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

    /// <summary>
    /// Defines the basic functionality for a call and response serial device. 
    /// The class is designed to allow XML import (though not nessecary). The goal here was to create a generic interface so that we could loop over devices
    /// </summary>
    public abstract class IMeasurementDevice
    {
        /// <summary>
        /// Stores whether the device has been initialized.
        /// </summary>
        protected bool _initialized;
        [XmlIgnore]//Don't want to accidently import 
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
        /// <summary>
        /// The device identifier. 
        /// </summary>
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
        /// <summary>
        /// The device name used for display
        /// </summary>
        public string DeviceName; //used for displaying device
        /// <summary>
        /// This is a template for the data to be read on a serial read
        /// </summary>
        public List<GenericReadResult> ReadDataTemplate { get; set; }
        [XmlIgnore]//Don't want to accidently import. We need to initialize this
        public DeviceSerial Port { get; set; }


        /// <summary>
        /// Funciton needed to parse data
        /// </summary>
        /// <param name="ReadWord">The read word.</param>
        /// <returns></returns>
        public delegate double ParseReadValDelegate(string ReadWord);

        /// <summary>
        /// Starts a serial read event. Can start read on each serial device then read later. Avoids waiting for response. 
        /// </summary>
        public virtual void KickOffRead()
        {
            Port.DiscardInBuffer();
            Port.WriteLine(Port.ReadCommand);
        }

        /// <summary>
        /// Reads the ready data. Meant to be the companion to KickOffRead()
        /// </summary>
        /// <param name="ParseReadVal">The function to parse the message.</param>
        /// <returns></returns>
        public virtual double ReadReadyData(ParseReadValDelegate ParseReadVal)
        {
            string message = Port.ReadExisting();
            return ParseReadVal(message);
        }

        /// <summary>
        /// Calls, waits, then reads availible data.
        /// </summary>
        /// <param name="ParseReadVal">The parse read value.</param>
        /// <returns></returns>
        public virtual double ReadVal(ParseReadValDelegate ParseReadVal)
        {
            Port.WriteLine(Port.ReadCommand);
            System.Threading.Thread.Sleep(30);
            string message = Port.ReadLine();
            return ParseReadVal(message);
        }

        /// <summary>
        /// Parses the read value set all.
        /// </summary>
        /// <param name="ReadWord">The read word.</param>
        /// <returns></returns>
        public abstract double ParseReadVal_SetAll(string ReadWord);
        /// <summary>
        /// Finds the correct port and initializes.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Sends a value to the device.
        /// </summary>
        /// <param name="Val">The value.</param>
        public abstract void SendVal(double Val);

        /// <summary>
        /// Initializes a new instance of the <see cref="IMeasurementDevice"/> class.
        /// </summary>
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


    /// <summary>
    /// An extenstion of the SerialPort class. Most notably, it makes it easy to dynamically find the port attached to a actual device
    /// </summary>
    /// <seealso cref="System.IO.Ports.SerialPort" />
    [Serializable]
    public class DeviceSerial : SerialPort
    {
        /// <summary>
        /// Set when a port is found
        /// </summary>
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
        /// <summary>
        /// The string used to recognize the port
        /// </summary>
        private readonly string CallString;
        /// <summary>
        /// The response string that we should get if we called the right device
        /// </summary>
        private readonly string ResponseString;
        /// <summary>
        /// The string sent to request data
        /// </summary>
        public readonly string ReadCommand;
        /// <summary>
        /// The used ports. Speeds up finding ports
        /// </summary>
        private List<string> UsedPorts = new List<string>();
        /// <summary>
        /// Seconds it took to find the port. useful for diagnostics
        /// </summary>
        [XmlIgnore]
        public double SecToFindPort { get; set; }
        #endregion

        /// <summary>
        /// Uses the call and response strings to find a port
        /// </summary>
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

                                    if (ResponseString.StartsWith(MsgStr))
                                    {
                                        if (MsgStr == ResponseString)
                                        {
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

    /// <summary>
    /// Gives a bunch of possible result types
    /// </summary>
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
        [XmlEnum("Power")]
        Power,
        [XmlEnum("Other")]
        Other
    }
}
