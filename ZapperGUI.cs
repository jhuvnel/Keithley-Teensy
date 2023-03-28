using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

// For the "Fake" Zapper at end of file.
using System.IO.Ports;
using static KeithleyCrosspoint.D;

namespace KeithleyCrosspoint
{
    public partial class ZapperGUI : Form
    {
#if COM0COM
        // For testing/debugging.
        FakeZapper fakezap = new FakeZapper();
#endif
        SafeSerialPort_WithResponses SER;

        // If desired, can request a particular Teensy by the Serial Number.
        public ZapperGUI(string SerialNumber_str="")
        {
            InitializeComponent();

            if (DesignMode)
                return;

            Show();

            //tprintf("This is some test text.\n  And some numbers: %d, %.2f.\nCan it handle multiple lines??\n   -- Maybe -- \n",
            //    123, 45.67);
            //tprintf("Lots...\n");
            //tprintf("more...\n");
            //tprintf("lines...\n");

            //int NL = 35;
            //for (int i = 0; i < NL; ++i)
            //    tprintf("Line number %d\n", NL - i);

            int height = ZapperTextBox.Height;
            int font_h = ZapperTextBox.Font.Height;
            float font_s = ZapperTextBox.Font.Size;

            //tprintf("Height in lines is about %d (%d pixels / %d font height)\n",
            //    (int)(height / font_h), height, font_h);

            tprintf("Searching USB devices for Teensy Zapper.  PLEASE WAIT A MOMENT...\n");

            //var SPMon = new SerialPortService(SerialNumber_str);
            var SPMon = new SerialPortService(SerialNumber_str, TryOpeningPort: false);
            //SPMon.ShowSerialPorts();
            SER = SPMon.SER;

#if COM0COM
            //=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>=>
            // For TESTING with Com0Com
            //
            tprintf("SWITCH BACK TO THE USB SERIAL!!!\n\n");
            SER = new SafeSerialPort_WithResponses();
            SER.PortName = "COM10";
            SER.Open();
            SER.SetSerDataCallback(SerGotData);
            //<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=<=
#endif
            SPMon.SetUSBCallback(USBCallback);
            SetZapperStatus(SER.IsOpen);
            SPMon.SER.SetSerDataCallback(SerGotData);

            Task.Run(() => SPMon.OpenIfAvailable());

            //tprintf("Done.\n");
        }

        private void SetZapperStatus(bool Available)
        {
            if (Available)
            {
                richTextBox1.BackColor = Color.Green;
                richTextBox1.Text = " Zapper Available ";
            }
            else
            {
                richTextBox1.BackColor = Color.Red;
                richTextBox1.Text = " <zapper unavailable> ";
            }
        }



        // Hold a list of stimulation parameters that we can look up and match.
        public class StimTable
        {
            const int MaxEntries = 2000;
            string[] StimStrings = new string[MaxEntries];

            // "Rectangular" 2D arrays are inconvenient in C#. Cannot simply address them as 1D arrays
            // as you can in C/C++.
            //public byte[,] StimBytes = new byte[MaxEntries, MaxStimBytes];
            // new byte[] arrays will be created in AddStr() as needed.
            public byte[][] StimBytes = new byte[MaxEntries][];
            int NEntries = 0;

            public void ClearAll()
            {
                StimStrings = new string[MaxEntries];
                StimBytes = new byte[MaxEntries][];
                NEntries = 0;
            }

            public int IndexOf(string findstr)
            {
                return Array.IndexOf(StimStrings, findstr);
            }

            // If the string is not already in the array, add it.
            // Return the index of the array entry for the string.
            // If the string is not in the array, and the array is already full, return -1.
            public int AddStr(string addstr)
            {
                var idx = IndexOf(addstr);
                if (idx >= 0)
                    return idx;
                if (NEntries >= MaxEntries)
                    return -1;
                StimStrings[NEntries] = addstr;
                return NEntries++;
            }
        }

        public StimTable STbl = new StimTable();
        public int CurStimIdx = 0;

        // Take the string written by the Writer and either look it up, or save
        // it, in our table. If we run out of room, default to index 0 and
        // printf() a complaint.
        public int SetCurrentStim(Int32[] StimParams)
        {
            if ((StimParams == null) || (StimParams.Length != 4))
                return CurStimIdx;

            string StimStr = sprintf("%d %d %d", StimParams[0], StimParams[1], StimParams[2]);
            var StrIdx = STbl.AddStr(StimStr);
            if (StrIdx < 0)
                tprintf("Ran out of room in Stim params table.\n");
            CurStimIdx = StrIdx >= 0 ? StrIdx : 0;
            tprintf("CurStimIdx: %d  (%d %d %d %d)\n",
                CurStimIdx, StimParams[0], StimParams[1], StimParams[2], StimParams[3]);
            return CurStimIdx;
        }

        public int SetCurrentStim(string StimStr)
        {
            if (StimStr == null)
                return CurStimIdx;

            var StrIdx = STbl.AddStr(StimStr);
            if (StrIdx < 0)
                tprintf("Ran out of room in Stim params table.\n");
            CurStimIdx = StrIdx >= 0 ? StrIdx : 0;
            tprintf("CurStimIdx: %d  (%s)\n", CurStimIdx, StimStr);
            return CurStimIdx;
        }

        // This one is called from outside the class, by the MVP code.
        //public void SelectStim(int i1, int i2, int i3, int i4, int i5, int i6)
        //
        // Using async/await Task.Delay() is MUCH better than using Thread.Sleep(), since
        // we need our GUI thread "awake" to handle possible events, like listening for bytes from
        // the Teensy. Thread.Sleep() is actually USELESS, because it does not allow the ScanZapperResponse()
        // to run to handle the LEARN_DONE event.
        //
        async public void SelectStim(string[] args)
        {
            // If current StimBytes[] entry is still empty, make sure last stim has
            // a chance to finish, and Zapper can send us the LEARN_DONE acknowledgement.
            if ((CurStimIdx >= 0) && (STbl.StimBytes[CurStimIdx] == null))
                //    Thread.Sleep(30);
                //await Task.Delay(40);
                await Task.Delay(10);

            int StimIdx = SetCurrentStim(String.Join(" ", args));
            //SER.TryWrite("T" + StimIdx.ToString("D2"));
            if (StimIdx >= 0)
            {
                CurStimIdx = StimIdx;
                // If the StimBytes[] entry is empty, that is our clue that the Teensy still
                // needs to LEARN that waveform, so we put the Teensy in LEARN mode. If it is
                // not empty, then we know it was learned previously, and that we should copy it
                // back to the Teensy now.
                if (STbl.StimBytes[StimIdx] == null)
                {
                    // Teensy still needs to LEARN this template.
                    SER.TryWrite("ClearAndLearnAll\n");
                    tprintf("LEARNing enabled for Template %d\n", StimIdx);
                }
                else
                {
                    CMD_StimToTeensy(STbl.StimBytes[StimIdx]);
                    tprintf("Sent Template %d to Zapper.\n", StimIdx);
                }
            }
        }

        public void tprintf(string format, params object[] args)
        {
            int MAX_LINES = ZapperTextBox.Height / ZapperTextBox.Font.Height - 1;
            ZapperTextBox.Text += Tools.sprintf(format, args).Replace("\n", Environment.NewLine);
            int NLines = ZapperTextBox.Lines.Count();
            if (NLines >= MAX_LINES)
                ZapperTextBox.Lines = ZapperTextBox.Lines.Skip(NLines - MAX_LINES).ToArray();
        }

        public static String sprintf(string format, params object[] args)
        { return Tools.sprintf(format, args); }

        private void ShowSerialStuff()
        {
            if (SER.BytesToRead > 0)
                tprintf("ZAPPER: %s", SER.TryRead());
        }

        // Look at text strings coming back from Teensy, and see if we find LEARN_DONE at
        // start of line.
        public SemaphoreSlim GotResponse_sem = new SemaphoreSlim(0, 1);
        private void ScanZapperResponse()
        {
            while (SER.BytesToRead > 0)
            {
                string Line = SER.ReadLine().Trim();
                if (Line.Length == 0)
                    return;
                string First = Line.Split(' ').First();
                //dprint($"*********** ZapperGUI: Got Response from Zapper. ('{Line}') ****************\n");
                if (First.Equals("LEARN_DONE") && (CurStimIdx >= 0))
                {
                    STbl.StimBytes[CurStimIdx] = CMD_StimFromTeensy();
                    dprint($"LEARNING DONE. Saved Zapper Template {CurStimIdx}\n");
                }
                else
                {
                    GotResponse_sem.Unblock();
                    dprint($"ZAPPER: {Line}\n");
                }
            }
        }

        bool DisableScanResponse = false;
        private void SerGotData()
        {
            // If we are waiting for binary bytes to read back the waveform, then don't scan for a response.
            if (DisableScanResponse)
                return;

            this.Invoke((MethodInvoker)delegate
            {
                //tprintf("Got serial data!");
                //ShowSerialStuff();
                ScanZapperResponse();
            });
        }

        private void USBCallback(bool ZapperAvailable)
        {
            // Must be sure to call on GUI thread if we want to update the GUI.
            this.Invoke((MethodInvoker)delegate
           {
               //tprintf("USB Callback called!!  Available: %s\n", ZapperAvailable.ToString());
               tprintf("USB Event: Zapper Available: %s\n", ZapperAvailable.ToString());
               SetZapperStatus(ZapperAvailable);
           });
        }

        private void ZapperGUI_KeyPress(object sender, KeyPressEventArgs e)
        {
            string key = e.KeyChar.ToString();
            //tprintf("Got a key!!! '%s'\n", key);
            //richTextBox1.Text = sprintf("Got a key: '%s'", e.KeyChar.ToString());

            switch (key)
            {
                case "1": SelectStim(new string[] { "1" }); break;
                case "2": SelectStim(new string[] { "2" }); break;
                case "3": SelectStim(new string[] { "3" }); break;

                case "G": if (CurStimIdx >= 0) STbl.StimBytes[CurStimIdx] = CMD_StimFromTeensy(); break;
                case "P": if (CurStimIdx >= 0) CMD_StimToTeensy(STbl.StimBytes[CurStimIdx]); break;
                //case "S": SER.TryWrite("SendLearnDone\n"); break;
                default: SER.TryWrite(key); break;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Write out the "C" command to clear all waveform templates,
            // and "L" to enable learning.
            STbl.ClearAll();
            //SER.TryWrite("clear_all\nlearn_all\n");
            SER.TryWrite("ClearAndLearnAll\n");
            tprintf("All Waveforms cleared. Learning enabled.\n");
        }

        // This stops the Error "ding" when a key is pressed while richTextBox is selected.
        // For some reason, richTextBox dings, and regular text box does not.
        private void richTextBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }

        public void dprintf(string format, params object[] args)
        {
            Debug.Write(Tools.sprintf(format, args));
        }

        public void CMD_StimToTeensy(byte[] BYTES)
        {
            if (!SER.IsOpen)
                return;

            if ((BYTES == null) || (BYTES.Length == 0))
                return;

            int NBytes = BYTES.Length;
            dprint($"SENDING WvToTeensy cmd. Writing {NBytes} bytes...\n");
            SER.TryWrite(Tools.sprintf("WvToTeensy %d\n", NBytes));
            SER.Write(BYTES, 0, NBytes);
        }

        public byte[] CMD_StimFromTeensy()
        {
            if (!SER.IsOpen)
                return null;

            // Make sure the serial data received handler does not jump in and take away our bytes.
            DisableScanResponse = true;

            // Request the Teensy to send us a waveform template.
            // Teensy first sends a numeric string telling how many BYTES, followed by a newline.
            dprint("SENDING WvFromTeensy cmd...\n");
            SER.TryWrite("WvFromTeensy\n");
            var str = SER.ReadLine().Trim();
            //dprintf("str: '%s'\n", str);
            int NBYTES = Convert.ToInt32(str);
            dprint($"  Going to read {NBYTES} bytes...\n");

            byte[] BYTES = new byte[NBYTES];
            int NREAD = 0;
            //NREAD = SER.Read(BYTES, 0, NBYTES);

            // For some unknown reason, need to loop here. I would have hoped that the
            // SerialPort class would wait until it returns the requested number of bytes,
            // but in testing, it does not, and it does not throw a timeout exception.
            // Seems like a bug.
            for (int i = 0; (NREAD < NBYTES) && (i < 20); ++i)
            {
                //NREAD += SER.BaseStream.Read(BYTES, NREAD, NBYTES);
                int rval = SER.Read(BYTES, NREAD, NBYTES - NREAD);
                NREAD += rval;
                //Thread.Sleep(1);
            }

            DisableScanResponse = false;

            //dprintf("Read %d bytes\n", NREAD);
            //dprint($"BYTES:  {Encoding.UTF8.GetString(BYTES)}\n");
            //dprint($"  Read {NREAD} bytes:  '{Encoding.UTF8.GetString(BYTES).Trim()}'\n");
            dprint($"  Read {NREAD} bytes.\n");

            /**
            dprintf("Buffer at 16-bit ints: %d, %d, %d, ..., %d, %d, %d, ...\n",
                BitConverter.ToInt16(BYTES, 4), BitConverter.ToInt16(BYTES, 6), BitConverter.ToInt16(BYTES, 8),
                BitConverter.ToInt16(BYTES, 100), BitConverter.ToInt16(BYTES, 102), BitConverter.ToInt16(BYTES, 104));
            **/
            //BitConverter.ToInt16(BYTES, NREAD - 6), BitConverter.ToInt16(BYTES, NREAD - 4), BitConverter.ToInt16(BYTES, NREAD - 2));
            return BYTES;
        }
    }


    // ===================================================================================================
    // ===================================================================================================
    // ===================================================================================================


    // Fake Keithley on COM 9/7.
    // Fake Zapper on COM 10/11.
    // Fake TeensyCrosspoint on COM 12/13.
    //
    // For testing, emulate a Keithley 6221 Current Source instrument.
    // Assumes that Com0Com is installed, and the "instrument" end is at COM11.
    //
    public class FakeZapper
    {
        SafeSerialPort SER;
        StringBuilder CmdStr = new StringBuilder(100);
        DateTime Time_last = DateTime.Now;
        int Delay_msec = 10; // How long we should take to respond.

        public FakeZapper()
        {
            CmdStr.Clear();

            SER = new SafeSerialPort();
            SER.PortName = "COM11";
            SER.Open();

            SER.DataReceived += DataReceivedHandler;
            // Get a callback from the Teensy Crosspoint when it receives a keithley_trigger command.
            FakeTeensyCrosspoint.SetTrigCallback(TeensyWasTriggered);
        }

        // After a Teensy "trigger" command, Delay() for some time, then send LEARN_DONE
        // back to the GUI.
        const int TriggerDelay_msec = 100;
        public void TeensyWasTriggered()
        {
            dprint("  FakeZapper: Got CALLBACK for keithley_trigger.\n");
            Task.Run(async () =>
            {
                var watch = Stopwatch.StartNew();
                await Task.Delay(TriggerDelay_msec);
                var elapse_msec = watch.ElapsedMilliseconds;
                dprint($"  FakeZapper: Sending LEARN_DONE (after {elapse_msec}mSec).\n");
                SER.TryWrite("LEARN_DONE\n");
            });
        }

        // Return TRUE if we processed a command.
        async private Task<bool> ProcessCmd(string CmdLine)
        {
            // Update the current volume.
            var Now = DateTime.Now;
            var Minutes = Now.Subtract(Time_last).TotalMinutes;
            Time_last = Now;

            string[] Args = CmdLine.Split(' ');
            int NArgs = Args.Length - 1;
            string Cmd = (Args.Length > 0 ? Args[0] : "").ToLower();
            bool CMDProcessed = true;
            bool DisplayCMD = true; // false;

            string Rsp = null; // The response string we want to send back, if any.
            switch (Cmd)
            {
                case "delay": if (NArgs >= 1) int.TryParse(Args[1], out Delay_msec); dprint($"  FAKE6221: delay={Delay_msec}\n"); Rsp = $"Delay set to {Delay_msec}mSec\n"; break;
                case "clearandlearnall": break;

                // Emulate a "fake" waveform of 10 bytes. We cleverly put a \n as the last
                // byte, so that when the PC sends it back to us, it is "properly
                // terminated" with a newline, as if it was a command, so does not require
                // "special" byte-by-byte handling here.
                case "wvfromteensy": dprint("  FakeZapper: WvFromTeensy...\n"); SER.TryWrite("10\n-WVBYTES-\n"); break;
                case "wvtoteensy": dprint($"  FakeZapper: WvToTeensy, {(NArgs>=1?Args[1]:"??")} bytes\n"); break;
                case "-wvbytes-": break;

                default: CMDProcessed = false; break;
            }

            if (DisplayCMD || !CMDProcessed)
                dprint($"  FakeZapper CMD: '{CmdStr}'{(CMDProcessed ? "" : "   <unknown cmd>")}\n");

            await Task.Delay(Delay_msec);  // "Processing" delay.

            if (Rsp != null)
                SER.TryWrite(Rsp);
            else if(Args.Length > 0)
                // The Zapper puts out a default response of the command with "COMPLETED:" prepended.
                SER.TryWrite($"COMPLETED: {Args[0]}\n");

            return CMDProcessed;
        }

        SemaphoreSlim Rx_lock = new SemaphoreSlim(1, 1);
        async private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            string str = SER.ReadExisting();

            await Rx_lock.WaitAsync();
            foreach (var ch in str)
            {
                if (ch == '\r' || ch == '\n')
                {
                    await ProcessCmd(CmdStr.ToString().Trim());
                    CmdStr.Clear();
                }
                else
                    CmdStr.Append(ch);
            }
            Rx_lock.Release();
        }
    }

}
