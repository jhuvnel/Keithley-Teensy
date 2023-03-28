using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static KeithleyCrosspoint.D;

// For the "Fake" Zapper at end of file.
using System.IO.Ports;


namespace KeithleyCrosspoint
{
    class TeensyDevice
    {
        // For testing/debugging.
#if COM0COM
        FakeTeensyCrosspoint faketeensy = new FakeTeensyCrosspoint();
#endif

        //SafeSerialPort SER = new SafeSerialPort();
        SafeSerialPort_WithResponses SER = new SafeSerialPort_WithResponses();

        public void OpenSerial(string portname)
        {
            if (SER?.IsOpen ?? false)
                return;

            // Get callbacks whenever we receive a newline-terminated response from the instrument.
            SER.SetSerResponseCallback(ResponseReceived);

            // SER = new SafeSerialPort();
            SER.WriteBufferSize = 4096 * 16;
            SER.ReadBufferSize = 4096 * 16;
            SER.ReadTimeout = 1000;
            SER.ReadTimeout = 1000;

            SER.PortName = portname;
            SER.Open();
        }

        public SemaphoreSlim GotResponse_sem = new SemaphoreSlim(0, 1);
        public void ResponseReceived(string rsp)
        {
            dprint($"TEENSY got response: '{rsp}'.\n");
            GotResponse_sem.Unblock();
        }

        public void SendCommands(string Commands)
        {
            dprint($"TEENSY Sending: '{Commands.Trim()}'\n");
            bool WasSent = SER.TryWrite(Commands);
            //dprint($"TEENSY WasSent {WasSent}, {(WasSent ? "" : "CLOSED! NOT ")}Sending: '{Commands.Trim()}'\n");
        }

    }


    // ===================================================================================================
    // ===================================================================================================
    // ===================================================================================================

    // Fake Keithley on COM 9/7.
    // Fake Zapper on COM 10/11.
    // Fake TeensyCrosspoint on COM 12/13.
    //
    // For testing, emulate TeensyCrosspoint, responding to commands.
    // Assumes that Com0Com is installed, and the "instrument" end is at COM13.
    //
    public class FakeTeensyCrosspoint
    {
        SafeSerialPort SER;
        StringBuilder CmdStr = new StringBuilder(100);
        DateTime Time_last = DateTime.Now;
        int Delay_msec = 10; // How long we should take to respond.

        // Other classes can inquire this semaphore to see when we have received a trigger command.
        // It is up to the other user to "Block()" the semaphore. We only "Unblock()" it here.
        //public static SemaphoreSlim GotTrigCmd_sem = new SemaphoreSlim(0, 1);

        // We also have a "callback" that another user can set to be called when we get a trigger.
        private static Action GotTrig_action = null;

        public static void SetTrigCallback(Action A)
        {
            GotTrig_action = A;
        }


        public FakeTeensyCrosspoint()
        {
            CmdStr.Clear();

            SER = new SafeSerialPort();
            SER.PortName = "COM13";
            SER.Open();

            SER.DataReceived += DataReceivedHandler;
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
                case "delay": if (NArgs >= 1) int.TryParse(Args[1], out Delay_msec); dprint($"  FAKETeensy: delay={Delay_msec}\n"); Rsp = $"Delay set to {Delay_msec}mSec\n"; break;
                case "*opc?": Rsp = "1\n"; break;
                case "keithley_trigger":
                    dprint("  FakeTeensy: got keithley_trigger cmd. Setting up delayed 'done' response...\n");
                    // Make a delay based on the trigger frequency and count.
                    if(NArgs >= 2)
                    {
                        double.TryParse(Args[1], out double Freq);
                        int.TryParse(Args[2], out int Count);
                        var DelayTask = Task.Run(async () =>
                        {
                            await Task.Delay((int)(1.0/Freq*1000*Count));
                            dprint($"  FakeTeensy: Sending KeithleyTriggerDone.\n");
                            SER.TryWrite("KeithleyTimerDone\n");
                        });
                    }
                    // GotTrigCmd_sem.Unblock();   // Not using this currently...
                    GotTrig_action?.Invoke();
                    break;
                case "set": break;
                case "clr": break;
                case "sendbits": break;
                default: CMDProcessed = false; break;
            }

            if (DisplayCMD || !CMDProcessed)
                dprint($"  FakeTeensy CMD: '{CmdStr}'{(CMDProcessed ? "" : "   <unknown cmd>")}\n");

            // A "processing" delay.
            await Task.Delay(Delay_msec);

            if (Rsp != null)
                SER.TryWrite(Rsp);

            return CMDProcessed;
        }

        SemaphoreSlim Rx_lock = new SemaphoreSlim(1,1);
        async private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            string str = SER.ReadExisting();
            //dprint($"FakeKeithleyGot: '{str}' (0x{Convert.ToByte(str[0]):X})\n");

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
