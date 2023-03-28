using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text;

using static KeithleyCrosspoint.D;

namespace KeithleyCrosspoint
{
    //
    // From http://stackoverflow.com/questions/13408476/detecting-when-a-serialport-gets-disconnected
    //
    public class SafeSerialPort : SerialPort
    {
        // For convenience of caller, keep track of whether the serial port was opened by
        // us. Unplugging the USB device will immediately Close the port, so we then loose
        // knowledge of whether it had previously been open. This information is useful in
        // the Device Removed event handler, since several events are fired on each
        // removal. On each read, _WasOpen is set to the current value of IsOpen.
        private bool _WasOpen;
        public bool WasOpen
        {
            set { _WasOpen = value; }
            get { bool rval = _WasOpen; _WasOpen = IsOpen; return rval; }
        }

        public virtual new void Open()
        {
            if (!IsOpen)
            {
                try { base.Open(); }  //catch(System.UnauthorizedAccessException) { dprintf("COULD NOT open COM port - already open by another process.\n"); return; }
                catch { dprint($"COULD NOT open COM port '{this.PortName}'.\n"); return; }
                GC.SuppressFinalize(this.BaseStream);
                WasOpen = true;
                dprint($" ---Serial port {this.PortName} opened---\n");
            }
        }

        public virtual new void Close()
        {
            if (IsOpen)
            {
                GC.ReRegisterForFinalize(this.BaseStream);
                base.Close();
                dprint($" ---Serial port {this.PortName} closed---\n");
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        public bool TryWrite(string str)
        {
            try { if (IsOpen) Write(str); return true; }
            catch (Exception ex)
            {
                if (ex is System.IO.IOException || ex is InvalidOperationException) { Close(); return false; }
            }
            return false;
        }

        public string TryRead()
        {
            try { return IsOpen ? ReadExisting() : null; } catch (System.InvalidOperationException) { Close(); return null; }
        }

        // Override the existing BytesToRead property with one that won't crash if the
        // SerialPort is closed.
        public new int BytesToRead
        {
            get
            {
                try { return IsOpen ? base.BytesToRead : 0; } catch (System.InvalidOperationException) { Close(); return 0; }
            }
        }
    }

    // This extends SafeSerialPort to add some helpers for asynchronously waiting for command responses.
    public class SafeSerialPort_WithResponses : SafeSerialPort
    {
        public SafeSerialPort_WithResponses()
        {
            DataReceived += DataReceivedHandler;
        }

        // Build up the response string as we get bytes from the serial port.
        StringBuilder ResponseStr = new StringBuilder(100);

        // Provide the response string to an await-ing task, if it is requested.
        TaskCompletionSource<string> ResponseString_tcs = null;

        // Allow the user to have their own callback when serial data arrives. BUT, it is
        // ONLY called if we are NOT currently awaiting a response. If the user calls
        // CommandAndResponse(), then the user callback is NOT called for the response. If
        // the user wants to read their own data (say, some binary data, or something like
        // that), then they call SendCommand() instead of CommandAndResponse().
        Action SerDataCallback = null;
        public void SetSerDataCallback(Action A) => SerDataCallback = A;

        // This Action is if the caller wants to be notified after a complete
        // newline-terminated response has been received, and to have that response passed
        // to them. So it is a little "higher level" than the above "raw data" callback.
        Action<string> SerResponseCallback = null;
        public void SetSerResponseCallback(Action<string> A) => SerResponseCallback = A;


        private void ScanResponse()
        {
            string str = ReadExisting();

            // Handy for debugging. Shows all characters as byte values.
            //    var bytes = Encoding.ASCII.GetBytes(chars);
            //    var as_str = BitConverter.ToString(bytes);
            //    txwrite($"\nBYTES: {as_str}\n");

            foreach (var ch in str)
            {
                switch (ch)
                {
                    // Newline terminates the Response string.
                    case '\n':
                    case '\r':
                        var Resp_str = ResponseStr.ToString().Trim();

                        // First, call the user callback, if it exists.
                        SerResponseCallback?.Invoke(Resp_str);

                        // Next, let SendCommands() know we got a response.
                        ResponseString_tcs?.TrySetResult(Resp_str);
                        ResponseStr.Clear(); break;

                    default: ResponseStr.Append(ch); break;
                }
            }
        }

        // Called when the serial port receives data.
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            do
            {
                // If there is no awaiting task, then just call the user's callback, if it is
                // not null.
                if ((ResponseString_tcs == null) && (SerResponseCallback == null))
                    SerDataCallback?.Invoke();
                else
                    // If a response is awaited, then scan in the characters.
                    do ScanResponse(); while (BytesToRead > 0);
            } while(BytesToRead > 0);
        }

        // Make a "critical section" around the command handling.
        SemaphoreSlim CMD_lock = new SemaphoreSlim(1,1);

        // Send a single command to the pump, and await the response.
        //
        // Do NOT include \n or \r in the command string!!
        //
        // AcquireSemaphore should only be set FALSE if the semaphore is acquired by the
        // caller!! It MUST be acquired somewhere, else things happen all out of order.
        //
        public async Task<string>
            CommandAndResponse(string cmd, int Timeout_msec = 500,
                               bool AcquireSemaphore = true)
        {
            string rspstr = null;

            if (AcquireSemaphore)
                await CMD_lock.WaitAsync();

            FlushSerial();  // Make sure no extra junk in Serial buffer.
            ResponseStr.Clear(); // Make sure no extra junk in StringBuilder from timed-out commands are left in buffer.
            
            // Setting ResponseString_tcs to non-null also tells the DataReceivedHandler to
            // start saving characters to the StringBuilder response buffer.
            ResponseString_tcs = new TaskCompletionSource<string>();
            TryWrite($"{cmd.Trim()}\n");
            var rsptask = ResponseString_tcs.Task;

            if (await Task.WhenAny(rsptask, Task.Delay(Timeout_msec)) == rsptask)
                rspstr = rsptask.Result;
            else
                dprint("CommandAndResponse() Timed Out.\n");

            ResponseString_tcs = null;

            if (AcquireSemaphore)
                CMD_lock.Release();

            return rspstr;
        }

        public int FlushSerial()
        {
            byte[] ByteBuf = new byte[1000];
            int TotalFlushed = 0;
            while (BytesToRead > 0)
                TotalFlushed += Read(ByteBuf, 0, ByteBuf.Length);
            if (TotalFlushed > 0) dprint($"Flushed {TotalFlushed} bytes.\n");
            return TotalFlushed;
        }
    }


        public class SerialPortService
    {
        private ManagementObjectSearcher SerialPortSearcher;
        private ManagementEventWatcher DevChange;
        public SafeSerialPort_WithResponses SER;

        Action<bool> USBEventCallback;

        string TeensySerialNumber_str = "";

        public SerialPortService(string SerialNumber="", bool TryOpeningPort=true)
        {
            SER = new SafeSerialPort_WithResponses();
            SER.WriteBufferSize = 4096 * 16;
            SER.ReadBufferSize = 4096 * 16;
            SER.ReadTimeout = 1000;
            SER.ReadTimeout = 1000;
            SER.BaudRate = 115200;
            SER.Parity = Parity.None;
            SER.DataBits = 8;
            SER.StopBits = StopBits.One;

            TeensySerialNumber_str = SerialNumber;

            // Set up the searcher just once, then we can re-use it as needed.
            SerialPortSearcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM WIN32_SerialPort");
            MonitorDeviceChanges();
            //OpenIfAvailable();
            //Task.Run(async () => await OpenIfAvailable());
            if(TryOpeningPort)
                Task.Run(() => OpenIfAvailable());
        }

        public void SetUSBCallback(Action<bool> A)
        {
            USBEventCallback = A;
        }

        // See if a Teensy is available. If so, open it with a SerialPort object.
        //public async Task<bool> OpenIfAvailable()
        public bool OpenIfAvailable()
        {
            SER.Close();
            string COMstr = null;
            //COMstr = await FindCOMPort();
            COMstr = FindCOMPort();
            if (COMstr == null)
                return false;
            SER.PortName = COMstr;
            SER.Open();
            string SNStr = TeensySerialNumber_str == "" ? "" : $" Serial#{TeensySerialNumber_str}";
            Debug.Write($"Found Teensy Zapper{SNStr}, SerialPort {COMstr}\n");

            // Let user know of change in status.
            USBEventCallback?.Invoke(SER.IsOpen);

            return SER.IsOpen;
        }

        // We call this when we are pretty sure the port should be there, so we keep
        // retrying a few times.
        public void OpenIfAvailable_TryHard()
        {
            for (int i = 0; i < 20; ++i)
            {
                OpenIfAvailable();
                if (SER.IsOpen)
                    break;
                Thread.Sleep(100);
                dprint("  ---TRYING AGAIN to Open port---\n");
            }
        }

        // This could be called when *any* old USB device is disconnected, so we need to make
        // sure that the Zapper is no longer available before we panic and close the COM port.
        // Return TRUE if we close the port.
        public bool CloseIfUnavailable()
        {
            string COMstr = FindCOMPort();

            // Port still there? Then don't close it.
            if ((COMstr != null) && COMstr.Equals(SER.PortName))
                return false;
            SER.Close();
            return true;
        }

        // If a Teensy is plugged in, return the COM port string for the first Teensy we
        // find. Can either search for the word "Teensy" in the device Caption string, or
        // look for the vid and pid in the PNPDeviceID string. Or both!
        //public async Task<string> FindCOMPort()
        public string FindCOMPort()
        {
            // Run the saved WMI search to find serial ports.
            var collection = SerialPortSearcher.Get();
            foreach (var dev in collection)
                //if (dev["Caption"].ToString().Contains("Teensy"))
                if (dev["PNPDeviceID"].ToString().ContainsAllNoCase("vid_16c0", "pid_0483", TeensySerialNumber_str))
                    return dev["DeviceID"].ToString();
            return null;
        }

        private void MonitorDeviceChanges()
        {
            dprint("Starting to listen for port changes...\n");

            var deviceArrivalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"); // +" WHERE EventType = 2";
            DevChange = new ManagementEventWatcher(deviceArrivalQuery);
            DevChange.EventArrived += (sender, eventArgs) => DeviceChangeHandler(sender, eventArgs);
            DevChange.Start();  // Start listening for events
        }

        public void CleanUp()
        {
            DevChange.Stop();
        }


        // Event type 2 is device added, 3 is device removed. 1 and 4 are Config Change and
        // Docking. This particular event, for some odd reason, does not provide
        // information on *which* device it is. So on a device "Arrival"/Add, we just
        // re-search the SerialPorts and see if the Teensy is there.
        private void DeviceChangeHandler(object sender, EventArrivedEventArgs e)
        {
            int EventType = Convert.ToInt32(e.NewEvent["EventType"]);
            dprint($"Got port change event {EventType} (IsOpen {SER.IsOpen})\n");
            if (EventType == 2)
            {
                //ShowSerialPorts();
                if (!SER.IsOpen)
                {
                    OpenIfAvailable_TryHard();
                    // Let our user know of the event.
                    USBEventCallback?.Invoke(SER.IsOpen);
                }
            }
            else if (EventType == 3)
            {
                // SER.Close();
                if (CloseIfUnavailable())
                {
                    // Let our user know of the event.
                    if (SER.WasOpen && USBEventCallback != null)
                        USBEventCallback?.Invoke(SER.IsOpen);
                }
            }
        }

        public void ShowSerialPorts()
        {
            if (SerialPortSearcher == null)
            {
                dprint("SerialPortSearcher is null.\n");
                return;
            }

            dprint("List of serial ports:\n");

            var collection = SerialPortSearcher.Get();

            //printf("%d items in collection.\n", collection.Count);
            foreach (var dev in collection)
                dprint($"   Caption: '{dev["Caption"]}', PNPDeviceID: '{dev["PNPDeviceID"]}', DeviceID: '{dev["DeviceID"]}'\n");
        }
    }


    internal static partial class TestProgram
    {
        // An "extension" method to see if one string contains all of the substr strings.
        // Can pass "substrs" as a list of separate string parameters, or as an array of strings.
        public static bool ContainsAll(this string stringToCheck, params string[] substrs)
        {
            return substrs.All(stringToCheck.Contains);
        }

        public static bool ContainsAllNoCase(this string stringToCheck, params string[] substrs)
        {
            return substrs.All(s => stringToCheck.ToLower().Contains(s.ToLower()));
        }
    }
}
