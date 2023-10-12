using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Diagnostics;

using static KeithleyCrosspoint.D;

// Simple driver to use the serial port to talk to the Keithley 6221.
// Sets up a basic 100uSec biphasic pulse using the Keithley's ARB
// (Arbitrary Waveform) function.

namespace KeithleyCrosspoint
{
    public static partial class D
    {
        // We provide our own timestamps, since the Visual Studio Debug output window
        // timestamps have a resolution of about 250mSec, which is too crude for our needs.
        static Stopwatch TxWatch = Stopwatch.StartNew();  // Free-running stopwatch for timestamps.
        static SemaphoreSlim TxBox_lock = new SemaphoreSlim(1,1);
        public static void dprint(string str)
        {
            //await TxBox_lock.WaitAsync();
            TxBox_lock.Wait(); // Let's do this non-async for now...

            //Debug.Write(Tools.sprintf(format, args));
            double sec = TxWatch.ElapsedMilliseconds / 1000.0;
            double min = Math.Floor(sec / 60.0);
            string tmstr = $"[{min}:{sec:00.000}] ";
            //Console.Write(tmstr + str);
            Debug.Write(tmstr + str);

            TxBox_lock.Release();
        }

        public static void dprint_ResetTimestamp() => TxWatch.Restart();

        // "Extension" methods to easily "block" and "unblock" a semaphore.
        // This can allow one thread to easily wait for some activity in another thread.
        // This "assumes" a Semaphore that has a Max "request" count of 1.
        // Reminder: Wait() will *decrement* the semaphore counter.
        //           Release() will *increment* the semaphore counter.
        public static void Block(this SemaphoreSlim sem)
        {
            if (sem.CurrentCount > 0)
                sem.Wait(0); // The zero timeout makes sure we don't get stuck here.
        }

        public static void Unblock(this SemaphoreSlim sem)
        {
            // In case someone jumps in the middle, we will catch the exception.
            if (sem.CurrentCount < 1)
                try { sem.Release(); } catch (SemaphoreFullException) { }
        }
    }

    class Keithley6221
    {
#if COM0COM
        // For testing/debugging.
        FakeKeithley6221 FakeK = new FakeKeithley6221();
#endif

        SafeSerialPort_WithResponses SER = new SafeSerialPort_WithResponses();

        void Init()
        {

        }

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
            SER.BaudRate = 115200;
            SER.Parity = Parity.None;
            SER.DataBits = 8;
            SER.StopBits = StopBits.One;

            SER.PortName = portname;
            SER.Open();
        }

        public SemaphoreSlim GotResponse_sem = new SemaphoreSlim(0,1);
        void ResponseReceived(string rsp)
        {
            if ((rsp == null) || rsp.Length < 1)
                return;

            // See if we got an "error" response. They look like this (two examples):
            //
            //   123,"Error Message In Quotes"
            //   0,"No error"
            //
            // You get a message number, a comma, and the message text in quotes. If there
            // is no error, you get exactly the second response shown. As a lame check,
            // just look for the final double-quote character.
            if(rsp[rsp.Length-1] == '\"')
            {
                // Don't display the "No error" error!
                if(!rsp.ToLower().Equals("0,\"no error\""))
                    dprint($"KEITHLEY ERROR  *****>>>>> {rsp} <<<<<*****\n");
            } else
                dprint($"KEITHLEY got response: '{rsp}'.\n");

            // Only Unblock when we receive the '1' response to the *opc? command.
            if (rsp[0] == '1')
                GotResponse_sem.Unblock();
        }

        public void SendCommands(string Commands)
        {
            dprint($"Keithley Sending: '{Commands.Trim()}'\n");
            bool WasSent = SER.TryWrite(Commands);
            //D.printf("WasSent %s, %sSending: '%s'\n", WasSent.ToString(), WasSent ? "" : "CLOSED! NOT ", Commands);
            //dprint($"WasSent {WasSent}, {(WasSent ? "" : "CLOSED! NOT ")}Sending: '{Commands.Trim()}'\n");
        }

        public async Task<string> CommandAndResponse(string cmd)
        {
            return await SER.CommandAndResponse(cmd);
        }

        // Generate the arbitrary waveform data points used by the Keithley. To keep things simple,
        // this is a VERY restricted process. The waveform must be EXACTLY 20 points long, with
        // EXACTLY 25uSec per data point, and each data point is -1, 0, or 1 (with the -1 phase
        // always first). As an example, this is a "typical" waveform, with 100uSec per phase, with a
        // 25uSec interphase gap.
        //
        // { -1, -1, -1, -1, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        public double[] GenPulseWaveform(int PhaseDur_us, int InterphaseGap_us)
        {
            double[] ArbData = new double[20];

            int NPhasePts = (int)Math.Round(PhaseDur_us / 25.0);
            int NGapPts = (int)Math.Round(InterphaseGap_us / 25.0);
            int Arb_idx = 0;

            // If the user passes bad values that cause us to exceed the array, then an exception
            // will be thrown.
            for (int i = 0; i < NPhasePts; ++i, ++Arb_idx)
                ArbData[Arb_idx] = -1;

            for (int i = 0; i < NGapPts; ++i, ++Arb_idx)
                ArbData[Arb_idx] = 0;

            for (int i = 0; i < NPhasePts; ++i, ++Arb_idx)
                ArbData[Arb_idx] = 1;

            dprint($"GenPulseWaveform({PhaseDur_us},{InterphaseGap_us}):  [{string.Join(",", ArbData)}]\n");

            return ArbData;
        }

        // For now, this is fixed at 100uSec per phase, with 25uSec inter-phase
        // gap.
        public async Task SetupPulse()
        {
            // Cathodic first!! Putting -1's puts out "negative current" first (the "High"
            // output terminal will have a more negative voltage than the "Low" output
            // terminal), then positive second.
            //double[] ArbData = { -1, -1, -1, -1, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            PREV_Phase1Dur_us = 100;
            PREV_Gap_us = 25;
            double[] ArbData = GenPulseWaveform(PREV_Phase1Dur_us, PREV_Gap_us);
            int NPoints = ArbData.Length;
            double ArbStep_usec = 25;
            // The ARB frequency should be 1/(25uSec*NPoints).
            double Freq_Hz = 1.0 / (NPoints * ArbStep_usec * 1.0e-6);
            double Amplitude_uA = 100;
            double Compliance_volts = 10;

            // Use a C# @"verbatim string" here, so we can have a bunch of lines without
            // having to do "this\n" + "that\n". Also use the $"interpolated string"
            // feature to intersperse expressions inside the string.
            string CMDstr = string.Join("\n"
                , "*rst"
                , "syst:beep:stat off"

                // Range is 2mA.
                , "curr:rang 2e-3"

                , $"curr:comp {Compliance_volts}"
                , "curr 0"
                , "sour:wave:extr:ival 0"
                , $"sour:wave:arb:data {string.Join(",", ArbData)}"
                , "sour:wave:func arb0"
                , $"sour:wave:freq {Freq_Hz}"
                , $"sour:wave:ampl {Amplitude_uA * 1e-6}"  // Convert uA to Amps
                , "sour:wave:offs 0"
                , "sour:wave:rang fix"

                // For quick "front panel" testing, set waveform to run continuously.
                //,"sour:wave:dur:cycl inf"

                // For "real" operation, use external trigger line #1 (from Teensy) to
                // trigger each output pulse (one waveform "cycle" per trigger pulse).
                , "sour:wave:dur:cycl 1"
                , "sour:wave:extrig:iline 1"
                , "sour:wave:extrig on"

                // Turn on phase marker output line.
                , "sour:wave:pmar:stat on"
                , "sour:wave:pmar 0"

                // Set the Keithley triax cable Inner Shield (cable BLACK) to GUARD voltage.
                // Connect the LOW output to earth ground (cable GREEN is earth ground).
                //, "outp:ishield guard"
                //, "outp:ltearth on"

                // Trigger output is SOURce. But we generally use the Phase Marker line,
                // not the Trigger Output line.
                //, "trig:outp sour"

                //,"outp on"
                , "sour:wave:arm"
                , "sour:wave:init"

                // Request a "1\n" response from the Keithley when all commands have completed.
                , "*opc?"

                , "" // MUST ALWAYS have a trailing newline!! string.Join() does not add last newline.
                );

            //SendCommands(CMDstr);
            await SER.CommandAndResponse(CMDstr, Timeout_msec:2000);
        }


        // Set the ARB waveform amplitude, in micro-amps.
        // The Keithley expects the value in Amps, so we scale by 1e-6.
        // Note, the SMALLEST amplitude in 2mA range seems to be 2uA, for some reason.
        public void SetAmplitude_uA(double Amplitude_uA)
        {
            // Limit to at most 2mA.
            if (Amplitude_uA > 2000)
                Amplitude_uA = 2000;
            string cmdstr = $"sour:wave:ampl {Amplitude_uA * 1e-6}\n";
            //Debug.Write($"SetAmplitude command: '{cmdstr}'\n");
            SendCommands(cmdstr);
            //SendCommands($"sour:wave:ampl {Amplitude_uA * 1e-6}\n");
        }

        int PREV_Phase1Dur_us, PREV_Phase2Dur_us;
        int PREV_Gap_us;

        /**************************
        // This can take a LONG time, like more than 1 second, so the caller
        // should BE SURE SET A TIMEOUT of at least 2000mSec!!!
        public void SetWaveform(int PhaseDur_us, int InterphaseGap_us)
        {
            if ((PhaseDur_us == PREV_PhaseDur_us) && (InterphaseGap_us == PREV_Gap_us))
                return;

            PREV_PhaseDur_us = PhaseDur_us;
            PREV_Gap_us = InterphaseGap_us;

            double[] ArbData = GenPulseWaveform(PhaseDur_us, InterphaseGap_us);
            SendCommands(
                "sour:wave:abor\n"
                +$"sour:wave:arb:data {string.Join(",", ArbData)}\n"
                +"sour:wave:arm\n"
                +"sour:wave:init\n"
                );
        }
        ******************/
        // Return TRUE for success.
        // Generate the arbitrary waveform data points used by the Keithley.
        // Uses 25uSec per output data point, 100 data points maximum.
        // Phase2 amplitude is ALWAYS computed to be "charge balanced" with the
        // first phase, so that duration*amplitude is equal for both phases.
        const int STEP_uSec = 25;
        //public bool SetWaveform_Asymm(int Phase1Dur_us, int InterphaseGap_us, int Phase2Dur_us)
        public bool SetWaveform(int Phase1Dur_us, int InterphaseGap_us, int Phase2Dur_us)
        {
            if ((Phase1Dur_us == PREV_Phase1Dur_us) && (Phase2Dur_us == PREV_Phase2Dur_us) &&
                   (InterphaseGap_us == PREV_Gap_us))
                return true;

            PREV_Phase1Dur_us = Phase1Dur_us;
            PREV_Phase2Dur_us = Phase2Dur_us;
            PREV_Gap_us = InterphaseGap_us;

            int Total_uSec = Phase1Dur_us + InterphaseGap_us + Phase2Dur_us;
            if (Total_uSec / STEP_uSec > 99)
            {
                dprint($"Waveform too long ({Total_uSec}uSec)!! Must be <{STEP_uSec * 99}uSec.\n");
                return false;
            }

            int NPoints = (Total_uSec / STEP_uSec) + 1; // Need to add a 0 at the end.
            double[] ArbData = new double[NPoints];
            int Arb_idx = 0;

            // Phase2 Amplitude is computed automatically for "charge balance".
            double Phase2Ratio = (double)Phase1Dur_us / Phase2Dur_us;

            for (int i = 0; i < (Phase1Dur_us / STEP_uSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = -1;

            for (int i = 0; i < (InterphaseGap_us / STEP_uSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = 0;

            for (int i = 0; i < (Phase2Dur_us / STEP_uSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = Phase2Ratio;

            ArbData[Arb_idx++] = 0;
            dprint($"#points {ArbData.Length}=={Phase1Dur_us / STEP_uSec}+{InterphaseGap_us / STEP_uSec}+{Phase2Dur_us / STEP_uSec}+1  "
                 + $"Waveform:  [{string.Join(",", ArbData)}]\n");

            // The ARB frequency should be 1/(25uSec*NPoints).
            double Freq_Hz = 1.0 / (NPoints * STEP_uSec * 1.0e-6);

            SendCommands(
                "sour:wave:abor\n"
                + $"sour:wave:arb:data {string.Join(",", ArbData)}\n"
                + $"sour:wave:freq {Freq_Hz}\n"
                + "sour:wave:arm\n"
                + "sour:wave:init\n"
                );
            return true;
        }


        int PREV_Phase1Dur_ms, PREV_Phase2Dur_ms;
        int PREV_Gap_ms;

        // Return TRUE for success.
        // Generate the arbitrary waveform data points used by the Keithley.
        // Uses 25uSec per output data point, 100 data points maximum.
        // Phase2 amplitude is ALWAYS computed to be "charge balanced" with the
        // first phase, so that duration*amplitude is equal for both phases.
        const int STEP_mSec = 40;
        //public bool SetWaveform_Asymm(int Phase1Dur_us, int InterphaseGap_us, int Phase2Dur_us)
        public bool SetWaveformDC(int Phase1Dur_ms, double Phase1Ampl_uA, int InterphaseGap_ms, int Phase2Dur_ms, double Phase2Ampl_uA)
        {
            if ((Phase1Dur_ms == PREV_Phase1Dur_ms) && (Phase2Dur_ms == PREV_Phase2Dur_ms) &&
                   (InterphaseGap_ms == PREV_Gap_ms))
                return true;

            PREV_Phase1Dur_ms = Phase1Dur_ms;
            PREV_Phase2Dur_ms = Phase2Dur_ms;
            PREV_Gap_ms = InterphaseGap_ms;

            int Total_uSec = Phase1Dur_ms + InterphaseGap_ms + Phase2Dur_ms;
            if (Total_uSec / STEP_mSec > 99)
            {
                dprint($"Waveform too long ({Total_uSec}uSec)!! Must be <{STEP_mSec * 99}uSec.\n");
                return false;
            }

            int NPoints = (Total_uSec / STEP_mSec) + 1; // Need to add a 0 at the end.
            double[] ArbData = new double[NPoints];
            int Arb_idx = 0;

            // DC "long pulses" are not necessarily "charge balanced".
            double Phase2Ratio = Phase1Ampl_uA / Phase2Ampl_uA;

            for (int i = 0; i < (Phase1Dur_ms / STEP_mSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = -1;

            for (int i = 0; i < (InterphaseGap_ms / STEP_mSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = 0;

            for (int i = 0; i < (Phase2Dur_ms / STEP_mSec); ++i, ++Arb_idx)
                ArbData[Arb_idx] = Phase2Ratio;

            ArbData[Arb_idx++] = 0;
            dprint($"#points {ArbData.Length}=={Phase1Dur_ms / STEP_mSec}+{InterphaseGap_ms / STEP_mSec}+{Phase2Dur_ms / STEP_mSec}+1  "
                 + $"Waveform:  [{string.Join(",", ArbData)}]\n");

            // The ARB frequency should be 1/(25uSec*NPoints).
            double Freq_Hz = 1.0 / (NPoints * STEP_mSec * 1.0e-3);  // should be 0.25 Hz (4s long intervals)

            SendCommands(
                "sour:wave:abor\n"
                + $"sour:wave:arb:data {string.Join(",", ArbData)}\n"
                + $"sour:wave:freq {Freq_Hz}\n"
                + "sour:wave:arm\n"
                + "sour:wave:init\n"
                );
            return true;
        }
    }


    static class DebugMain
    {

        // Can have async Main() only with compiler version 7.1 and higher.
        //static async Task Main()

        // Use csc command line option -define:DEBUG_MAIN to compile this in. So it would look like:
        //    csc -define:DEBUG_MAIN Keithley6221.cs SerialPortService.cs
        //
#if DEBUG_MAIN
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }
#endif
        static async Task MainAsync(string[] Args)
        {
            dprint("Hello from test Main()!!\n");
            var Keithley = new Keithley6221();
            Keithley.OpenSerial("COM9");

            string Line="";
            while(true)
            {
                Line = Console.ReadLine().Trim();
                if (Line.Length == 0)
                    continue;
                if (Line.ToLower() == "q")
                    break;
                string Resp = await Keithley.CommandAndResponse(Line);
                dprint($"CMDResponse: '{Resp}'\n");
            }
        }
    }


    // ===================================================================================================
    // ===================================================================================================
    // ===================================================================================================

    //
    // For testing, emulate a Keithley 6221 Current Source instrument.
    // Assumes that Com0Com is installed, with PC & Instrument on COM 9 & 7 respectively.
    //
    public class FakeKeithley6221
    {
        SafeSerialPort SER;
        StringBuilder CmdStr = new StringBuilder(100);
        DateTime Time_last = DateTime.Now;
        int Delay_msec = 10; // How long we should take to respond.

        public FakeKeithley6221()
        {
            CmdStr.Clear();

            SER = new SafeSerialPort();
            SER.PortName = "COM7";
            SER.Open();

            SER.DataReceived += DataReceivedHandler;
        }

        int count = 0;
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
                case "delay": if (NArgs >= 1) int.TryParse(Args[1], out Delay_msec); dprint($"  FAKE6221: delay={Delay_msec}\n");  Rsp = $"Delay set to {Delay_msec}mSec\n";  break;
                case "*opc?":
                    Rsp = "1\n";
                    // For debugging, allow caller to specify an extra delay for *opc?.
                    if (NArgs >= 1 && int.TryParse(Args[1], out int delay))
                    {
                        dprint($"  Fake6221 - *opc? extra {delay}mSec delay...\n");
                        await Task.Delay(delay);
                    }
                    break;
                case "*rst": break;
                case "syst:err?": Rsp = ((++count) % 3) == 0 ? "-123,\"Fake Error Message!!\"\n" : "0,\"No error\"\n"; break;
                case var s when s.StartsWith("sour:"): break;
                case var s when s.StartsWith("syst:"): break;
                case var s when s.StartsWith("curr:"): break;

                // Responses that take different amounts of time.
                case "r1": Rsp = "1\n"; break;
                case "r2": Rsp = "This is a longer response. So there.\n"; break;
                case "r3":
                    SER.TryWrite($"Response with {Delay_msec}mSec delay in middle >>.");
                    await Task.Delay(Delay_msec);
                    SER.TryWrite("<< This is the end of the response.\n");
                    break;

                default: CMDProcessed = false; break;
            }

            if (DisplayCMD || !CMDProcessed)
                dprint($"  Fake6221 CMD: '{CmdStr}'{(CMDProcessed ? "" : "   <unknown cmd>")}\n");

            // Have a delay before the response.
            await Task.Delay(Delay_msec);

            if (Rsp != null)
                SER.TryWrite(Rsp);

            // If we have a Query command (ends with '?' character), then put out some
            // dummy response.
            // Actually, just handle this in the command switch() above...
            //if (CmdLine[CmdLine.Length - 1] == '?')
            //{
            //    dprint("  Fake62221: Writing response '1' to serial port...\n");
            //    SER.TryWrite("1\n");
            //}

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
                if (ch == '\r' || ch =='\n')
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
