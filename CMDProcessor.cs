using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Media;

using static KeithleyCrosspoint.D;

namespace KeithleyCrosspoint
{    
    public partial class CMDProcessor : UserControl
    {

        Keithley6221 K6221 = new Keithley6221();
        TeensyDevice Teensy = new TeensyDevice();
        ZapperGUI Zapper = new ZapperGUI("2789010");  // Request a particular Teensy by serial number.
        CEDTalker.CEDTalker CEDTalker = new CEDTalker.CEDTalker();

        // Use "virtual" COM0COM port COM59 to send to COM19, which is set as a TextMark channel in
        // the CED Spike2 software. We use this to log each stimulation to the Spike2 data files.
        SafeSerialPort SER_ToSpike2 = new SafeSerialPort();

        /*
        public void dprintf(string format, params object[] args)
        {
            Debug.Write(Tools.sprintf(format, args));
        }
        */

        System.Windows.Forms.Timer CMD_Timer = new System.Windows.Forms.Timer(); // timer to run commands
        StreamWriter StimLogFile;

        SharedMemory sharedmem = new SharedMemory();

        // NEVER do any "real" initialization here, of hardware, etc.

        // THIS GETS CALLED AT "DESIGN TIME"!!!!! That is, the Visual Studio GUI may call
        // this at any time to (re)render the Form/Control in the design view. So DO NOT
        // open COM ports, etc., here.
        public CMDProcessor()
        {
            InitializeComponent();
			if (DesignMode)
				return;

			SetRunPauseButton(false);
            NumTrainsTimer.Tick += new EventHandler(NumTrainsTimer_handler);
            NumTrainsTimer.Stop();

            var DIR = Properties.Settings.Default.OpenFile_directory;
            //StimLogFile = new StreamWriter($"{DIR}\\StimLogFile_{DateTime.Now:yyyy_MMdd_HHmmss}.txt");
            //StimLogFile = new StreamWriter($"StimLogFile_{DateTime.Now:yyyy_MMdd_HHmmss}.txt");
            StimLogFile = new StreamWriter($"\\\\10.16.39.7\\labdata\\Monkey Single Unit Recording\\StimulationLog\\StimLogFile_{DateTime.Now:yyyy_MMdd_HHmmss}.txt");

            // Used to send "comment" strings to Spike2, to document stimulation parameters in the data files.
            // We use COM0COM "virtual" serial port driver to connect COM59 to COM19.
            // In Spike2 COM19 is set on the TextMark Channel 30.
            SER_ToSpike2.PortName = "COM59";
            SER_ToSpike2.WriteTimeout = 10; // Short time-out, so we don't hold things up.
            SER_ToSpike2.Open();

            Disposed += OnDispose;
        }

        private void OnDispose(object sender, EventArgs e)
        {
            // MUST disconnect from the Spike2 Talker, else Spike2 thinks that we
            // remain connected, even after this program terminates.
            CEDTalker.Disconnect();
        }

        // This clever trick (overriding ProcessCmdKey()) lets us get an "early peek" at
        // the key press, which is not usually easily allowed in a UserControl.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            //dprintf("In ProcessCmdKey, key: '%s'\n", keyData.ToString());

            if (keyData == Keys.Space)
            {
                SetRunPauseButton(!CMD_ListRunning);
            }
            else if (keyData == Keys.F1)
            {
                SoundPlayer simpleSound = new SoundPlayer(@"C:\Users\VNEL MOOG\Desktop\Click-16-44p1-mono-0.2secs.wav");
                int timesPlayed = 0;
                while (timesPlayed <251)
                {
                    simpleSound.Play();
                    timesPlayed = timesPlayed + 1;
                    SER_ToSpike2.WriteLine($"Click{timesPlayed}");
                    System.Threading.Thread.Sleep(250);

                }
                
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        
        // Just for debugging, initializing junk for testing.
        private async void InitButton_MouseClick(object sender, MouseEventArgs e)
        {
            await KeithleyInit("COM17");
            Teensy.OpenSerial("COM18");
        }

        private void RunPauseButton_MouseClick(object sender, MouseEventArgs e)
        {
            SetRunPauseButton(!CMD_ListRunning);
            //dprintf("Registering keypress handler...\n");
        }

        // See if the command processor is running or paused, or if it has no trials
        // loaded, and set the button appearance accordingly.
        
        string RunningText = "CLICK or press SPACE Bar to Pause\n(now RUNNING)";

        private void SetRunPauseButton(bool Running)
        {
            CMD_ListRunning = Running;

            // If trial list is empty, then disable this button.
            if (listBox1.DataSource == null)
            {
                CMD_ListRunning = false;
                RunPauseButton.BackColor = Color.LightGray;
                RunPauseButton.Text = "<no trials loaded>";
                RunPauseButton.Enabled = false;
                return;
            }

            RunPauseButton.ForeColor = Color.White;
            RunPauseButton.Enabled = true;

            if (CMD_ListRunning)
            {
                RunPauseButton.BackColor = Color.Green;
                RunPauseButton.Text = RunningText;
            }
            else
            {
                RunPauseButton.BackColor = Color.Red;
                RunPauseButton.Text = "CLICK or press SPACE Bar to Run\n(now PAUSED)";
                NumTrainsTimer.Stop();
                CMD_Running = false;
            }
        }



        private void OpenFile_MouseClick(object sender, MouseEventArgs e)
        {
            openTrialFile(sender, e);
        }

        //
        // Open and read in a trial file.
        // Remembers and saves the most recently used directory.
        //
        private void openTrialFile(object sender, EventArgs e)
        {
            // Set up a timer to continuously run commands.
            CMD_Timer.Tick += new EventHandler(CMD_TimerHandler);
            CMD_Timer.Interval = 20;
            CMD_Timer.Start();

            Stream myStream = null;
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            //            openFileDialog1.InitialDirectory = "c:\\";
            // Read in the last used directory.
            openFileDialog1.InitialDirectory = Properties.Settings.Default.OpenFile_directory;

            openFileDialog1.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            openFileDialog1.FilterIndex = 2;
            openFileDialog1.RestoreDirectory = true;

            // Stop the current list from running.
            SetRunPauseButton(false);

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                // Save this currently used directory.
                Properties.Settings.Default.OpenFile_directory = Path.GetDirectoryName(openFileDialog1.FileName);
                Properties.Settings.Default.Save();
                //Debug.WriteLine("Opened {0}, saving path {1}", openFileDialog1.FileName, Properties.Settings.Default.OpenFile_directory);

                try
                {
                    if ((myStream = openFileDialog1.OpenFile()) != null)
                    {
                        using (myStream)
                        {
                            // Read the trials into the GUI list box.
                            ReadTrialFile(new StreamReader(myStream));
                            listBox1.SelectedIndex = 0;
                            SetRunPauseButton(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: Could not read file from disk. Original error: " + ex.Message);
                }
            }
        }

        // Do the actual reading in of the trial lines from the text file.
        // Ignore blank lines, and any lines that don't start with a letter.
        // This allows you to put comments in the file.
        private bool ReadTrialFile(StreamReader s)
        {
            string line;

            ArrayList trials = new ArrayList();

            while ((line = s.ReadLine()) != null)
            {
                line = line.Trim();

                // Ignore any lines that do not start with a letter.
                // Actually, leave all lines in, so you can see any comments while it is running.
                //if (!char.IsLetter(line[0]))
                //    continue;

                trials.Add(line);
            }

            listBox1.DataSource = trials;
            return true;
        }

        //
        // Process the list of conmmands that we read in.
        //
        private DateTime DelayUntil = default(DateTime);
        private double DefaultDelay = 0; //2.0; //seconds

        //private enum CMDState { DISABLED, CMD_STARTED, CMD_FAILED, DELAY, }
        private bool CMD_ListRunning = false;
        private bool CMD_Running = false;
        private bool CMD_Done = false;
        private bool CMD_Issued = false;

        // Make sure the current line is not a comment, or blank.
        private bool GoodCMDLine()
        {
            string s = listBox1.SelectedItem.ToString();
            return (s.Length > 0) && char.IsLetter(s[0]);
        }

        // Increment the command pointer, but not beyond the end of the list.
        // Skip any "blank" or non-command lines.
        private bool NextCMD()
        {
            string s;

            do
            {
                if (listBox1.SelectedIndex == listBox1.Items.Count - 1)
                {
                    SetRunPauseButton(false);
                    return false;
                }
                ++listBox1.SelectedIndex;
            } while (!GoodCMDLine());

            return true;
        }

        // Called by a timer.
        private void CMD_TimerHandler(object sender, EventArgs e)
        {
            CMD_Processor();
        }

        // The command processor is called continuously by a timer event,
        // so it must use global booleans to keep track of what it is doing.
        //
        async private void CMD_Processor()
        {
            if (!CMD_ListRunning)
            {
                // If we pause the command processor, also clear out the Moog Running flag.
                CMD_Running = false;
                CMD_Issued = false;
                return;
            }

            // See if command is still executing.
            // Wait for Moog state machine above to set the CMD_Done flag.
            if (CMD_Running)
            {
                // CMD_Done is set by the Moog state machine above AFTER a command
                // has completed.
                if (!CMD_Done)
                    // If command is still running, just return.
                    return;
                else
                {
                    // When Moog goes IDLE, command is done.
                    CMD_Running = false;

                    // Add a default delay (which also advances to the next command).
                    DelayUntil = DateTime.Now.AddSeconds(DefaultDelay);
                }
            }

            // If we are in a delay, don't do any processing.
            if (DelayUntil != default(DateTime))
            {
                if (DateTime.Now.CompareTo(DelayUntil) < 0)
                    return;
                DelayUntil = default(DateTime);
            }

            // If we have already issued a command, then go to the next one.
            // Else, we are just starting up, so run the current command w/out
            // incrementing command list index.
            if (CMD_Issued || !GoodCMDLine())
            {
                if (!NextCMD())
                    return;
                // NextCMD() clears the CMD_Issued boolean when it increments the list index,
                // so we must re-set it.
                CMD_Issued = true;
            }

            string CMDstr = listBox1.SelectedItem.ToString();
            //Debug.WriteLine("CMD processor at index {0}: '{1}'", listBox1.SelectedIndex, CMDstr);

            // If we have a "bad" command, then stop the command processor.
            if (! await HandleCommandLine(CMDstr))
                SetRunPauseButton(false);

            // Remember that we issued this command, so next time around, increment the command
            // list index.
            CMD_Issued = true;
        }

        // This function handles a single command line string.
        // This function only STARTS commands, it does not guarantee that the commands
        // actually finish.
        //
        // It returns "false" when the command line string itself is bad, not necessarily
        // when the command does not execute properly.
        //
        DateTime CMD_DateTime; // Timestamp used for the current command.

        async private Task<bool> HandleCommandLine(string CMDLine)
        {
            string[] tokens = CMDLine.Split();
            bool rval = true;

            //dprintf("Command Line (%d): '%s'\n", CMDLine.Length, CMDLine);
            //dprint($"Command Line ({CMDLine.Length}): '{CMDLine}'\n");
            dprint($"Command Line: '{CMDLine}'\n");

            CMD_DateTime = DateTime.Now;

            // See what the command is.
            switch (tokens[0].ToLower())
            {
                case "pause":
                    // If we start on a Pause command, then skip it.
                    // Otherwise we get "stuck" on the Pause command.
                    if (CMD_Issued)
                        SetRunPauseButton(false);
                    break;

                case "delay_sec":
                    var Delay_sec = Convert.ToDouble(tokens[1]);
                    DelayUntil = DateTime.Now.AddSeconds(Delay_sec);
                    //Debug.WriteLine("Delay until {0}, now {1}", DelayUntil, DateTime.Now);
                    dprint($"Delay for {Delay_sec:0.000}sec\n");
                    break;

                case "delay_msec":
                    DelayUntil = DateTime.Now.AddSeconds(Convert.ToDouble(tokens[1])/1000.0);
                    Debug.WriteLine("Delay until {0}, now {1}", DelayUntil, DateTime.Now);
                    break;

                case "setdefaultdelay":
                    DefaultDelay = Convert.ToDouble(tokens[1]);
                    break;


                // CED Talker to open and close CED files.
                case "ced_connect": CEDTalker.CEDConnect(); break; 
                //case "ced_base_path": CEDTalker.CEDSetPath(tokens[1]); break;
                case "ced_base_path": CEDTalker.CEDSetPath(tokens); break;
                case "ced_configload": CEDTalker.CEDConfigLoad(tokens[1]); break;
                case "ced_open": CEDTalker.CEDOpen(); break;
                case "ced_close_and_save": CEDTalker.CEDCloseAndSave(filenameTextBox.Text, tokens[1]); break;
                case "ced_run_script": CEDTalker.CEDRunScript(tokens); break;
                case "ced_send_string": CEDTalker.CEDSendString(tokens); break;


                case "setampl": SetKeithleyAmpl(Convert.ToDouble(tokens[1])); break;

                case "keithley_stim":
                    await KeithleyStim(
                        StimDur_msec: Convert.ToInt32(tokens[1]),
                        StimGap_msec: Convert.ToInt32(tokens[2]),
                        StimRate_pps: Convert.ToDouble(tokens[3]),
                        NumPulseTrains: Convert.ToInt32(tokens[4]),

                        Phase1Dur_usec: Convert.ToInt32(tokens[5]),
                        InterPhaseGap_usec: Convert.ToInt32(tokens[6]),
                        Phase2Dur_usec: Convert.ToInt32(tokens[5]),  // Equal to Phase1 duration.

                        StimE: Convert.ToInt32(tokens[7]),
                        RefE: Convert.ToInt32(tokens[8]),
                        Phase1Ampl_uA: Convert.ToInt32(tokens[9])
                        );
                    break;

                case "keithley_stim_asymmetric":
                    await KeithleyStim(
                        StimDur_msec: Convert.ToInt32(tokens[1]),
                        StimGap_msec: Convert.ToInt32(tokens[2]),
                        StimRate_pps: Convert.ToDouble(tokens[3]),
                        NumPulseTrains: Convert.ToInt32(tokens[4]),

                        Phase1Dur_usec: Convert.ToInt32(tokens[5]),
                        InterPhaseGap_usec: Convert.ToInt32(tokens[6]),
                        Phase2Dur_usec: Convert.ToInt32(tokens[7]),

                        StimE: Convert.ToInt32(tokens[8]),
                        RefE: Convert.ToInt32(tokens[9]),
                        Phase1Ampl_uA: Convert.ToDouble(tokens[10])
                        );
                    break;

                case "keithley_open": K6221.OpenSerial(tokens[1]); break;
                case "keithley_init": await KeithleyInit(tokens[1]); break;
                case "teensy_open": Teensy.OpenSerial(tokens[1]); break;

                case "baseline": Teensy.SendCommands($"baseline {Convert.ToDouble(tokens[1])}\n"); break;
                case "sinemod":
                    SetSineMod(
                             Convert.ToDouble(tokens[1]), // Sine freq
                             Convert.ToDouble(tokens[2]), // +/- delta pulse freq
                             Convert.ToInt32(tokens[3])); // Number of sine cycles
                    break;
                case "gyromode": Teensy.SendCommands($"gyromode {Convert.ToDouble(tokens[1])}\n"); break;

                case "startrecording":
                    string filename = DateTime.Now.ToString("yyyyMMdd_HHmmss") + Pre_(filenameTextBox.Text) + Pre_(tokens[1]);
                    sharedmem.OpenFile(filename + ".coil");
                    sharedmem.StartCollecting();
                    break;

                case "stoprecording":
                    sharedmem.StopCollecting();
                    sharedmem.CloseFile();
                    break;
                /*
                case "email":
                    SendEmail(CMDLine);
                    break;

                // MOOG commands.
                case "rotation":
                    rval = CMD_Rotation(tokens);
                    break;

                case "translation":
                    rval = CMD_Translation(tokens);
                    break;

                case "tilt":
                    rval = CMD_StaticTilt(tokens);
                    break;
               */
                default:
                    dprint("BAD Script Command!!\n");
                    return false;
                    break;
            }

            //StimLogFile.WriteLine($"{DateTime.Now:yyyy/MM/dd HH:mm:ss.ff}:  {CMDLine}");
            StimLogFile.WriteLine($"{CMD_DateTime:yyyy/MM/dd HH:mm:ss.fff}:  {CMDLine}");
            StimLogFile.Flush();

            return rval;
        }

        // Prepend an underscore to a string, but only if it is not empty.
        private string Pre_(string s)
        {
            if (s.Length > 0)
                return "_" + s;
            return "";
        }

        private async Task KeithleyInit(string portname)
        {
            dprint($"KeithleyInit({portname})\n");

            K6221.OpenSerial(portname);
            await K6221.SetupPulse();
            //K62221.SetAmplitude_uA(1); // Set up some small default current.
        }

        async private Task KeithleyStim(
            int StimDur_msec,         // Duration of the pulse train.
            int StimGap_msec,         // Time between pulse trains.
            double StimRate_pps,         // Stim rate, pulses per second.
            int NumPulseTrains,          // Number of pulse trains.

            int StimE,                   // Stimulation electrode number.
            int RefE,                    // Reference electrode number.
            double Phase1Ampl_uA,         // Stim amplitude, micro-amps

            int Phase1Dur_usec = 100,     // Duration of each stim phase, typically 100uSec.
            int InterPhaseGap_usec = 25,  // Gap between first and second phases.
            int Phase2Dur_usec = 100      // Duration of each stim phase, typically 100uSec.
            )
        {
            dprint($"stim_asymm: {StimDur_msec}/{StimGap_msec}/{StimRate_pps:f0}/{NumPulseTrains}  "
                  + $"E{StimE}:E{RefE}  "
                  + $"{Phase1Ampl_uA}uA {Phase1Dur_usec}uS:{InterPhaseGap_usec}uS:{Phase2Dur_usec}uS\n"
                );

            CMD_Running = true;

            dprint_ResetTimestamp();

            /*
            // Log to CED Spike2 software.
            var StimInterval_ms = 1 / StimRate_pps * 1000.0;
            int NStim = (int)(StimDur_msec * StimRate_pps / 1000.0);
            string TimeStr = $"{CMD_DateTime:HH:mm:ss.fff}";

            try
            {
                SER_ToSpike2.WriteLine($"Stm{StimE} Ref{RefE} {Phase1Ampl_uA:.0}uA {NumPulseTrains}x({NStim}x{StimInterval_ms}mS+{StimGap_msec}mS_Gap)  {Phase1Dur_usec}/{InterPhaseGap_usec}/{Phase2Dur_usec}uSec [{TimeStr}]");
            }
            catch (TimeoutException) { }
            */

            // Read and clear the Keithley error queue. This will NOT unblock the semaphore.
            K6221.SendCommands("syst:err?\n" + "*cls\n");

            // SetWaveform really only re-programs the Keithley waveform if it is different
            // fromt the last time it was called.
            //K6221.SetWaveform(PhaseDur_usec, InterPhaseGap_usec);
            K6221.SetWaveform(Phase1Dur_usec, InterPhaseGap_usec, Phase2Dur_usec);
            K6221.SetAmplitude_uA(Phase1Ampl_uA);
            K6221.GotResponse_sem.Block();
#if COM0COM
            K6221.SendCommands("*opc? 100\n");
#else
            K6221.SendCommands("*opc?\n"); // Request a response from the Keithley.
#endif

            Zapper.GotResponse_sem.Block();
            // Tell Zapper about the waveform.
            Zapper.SelectStim(new string[] {
                Phase1Dur_usec.ToString(), InterPhaseGap_usec.ToString(),
                Phase1Ampl_uA.ToString(), StimE.ToString(), RefE.ToString()
            });

            Teensy.SendCommands($"clr\nset {RefE} 14\nset {StimE} 15\nsendbits\n");
            // Allow some time for the Keithley to process its command.
            Thread.Sleep(15);
            //Teensy.SendCommands($"keithley_trigger {StimRate_pps} {StimDur_msec*StimRate_pps/1000.0}\n");
            TeensyKeithleyTrigger_string = $"keithley_trigger {StimRate_pps} {StimDur_msec * StimRate_pps / 1000.0}\n";

            dprint("AWAITing Keithley response before sending trigger...\n");
            // This will remain blocked until the Keithley receives its 1\n response from
            // the *opc? command (or until it times out).
            await K6221.GotResponse_sem.WaitAsync(millisecondsTimeout: 2000);

            dprint("AWAITing Zapper response...\n");
            await Zapper.GotResponse_sem.WaitAsync(200);

            // Log to CED Spike2 software.
            var StimInterval_ms = 1 / StimRate_pps * 1000.0;
            int NStim = (int)(StimDur_msec * StimRate_pps / 1000.0);
            string TimeStr = $"{CMD_DateTime:HH:mm:ss.fff}";

            try
            {
                SER_ToSpike2.WriteLine($"Stm{StimE} Ref{RefE} {Phase1Ampl_uA:.0}uA {NumPulseTrains}x({NStim}x{StimInterval_ms}mS+{StimGap_msec}mS_Gap)  {Phase1Dur_usec}/{InterPhaseGap_usec}/{Phase2Dur_usec}uSec [{TimeStr}]");
            }
            catch (TimeoutException) { }

            // We always do the first trigger here, and subsequent ones in the timer routine.
            //Teensy.SendCommands(TeensyKeithleyTrigger_string);
            // NOW using Spike2/CED to send the Stim pulses, to keep in sync with CED analog samples.
            CEDTalker.SendString(TeensyKeithleyTrigger_string);
            if (BlankingTrigger.Checked == true)
            {
                bool hi = true;
            }

            NumPulseTrains_cntdn = NumPulseTrains;
            NumTrainsTimer.Interval = StimDur_msec + StimGap_msec;
            NumTrainsTimer.Start();

            if (NumPulseTrains_cntdn > 1)
                UpdateRunningText();

            // Wait for the pulse train to finish.
            //Thread.Sleep(StimDur_msec + StimGap_msec);
        }

        private void SetSineMod(double SineFreq, double DeltaPulseFreq, Int32 NCycles)
        {
            Teensy.SendCommands($"sinemod {SineFreq} {DeltaPulseFreq} {NCycles}\n");
            var Delay_sec = NCycles / SineFreq;
            DelayUntil = DateTime.Now.AddSeconds(Delay_sec);
        }

        private void UpdateRunningText()
        {
            if (NumPulseTrains_cntdn > 0)
                RunPauseButton.Text = $"{RunningText}  StimTrains:{NumPulseTrains_cntdn}";
            else
                RunPauseButton.Text = RunningText;
        }

        // Save the Teensy command string here, so we can send it a bunch of times if NumPulseTrains is >1.
        string TeensyKeithleyTrigger_string;
        System.Windows.Forms.Timer NumTrainsTimer = new System.Windows.Forms.Timer(); // timer to run commands
        int NumPulseTrains_cntdn;

        private void NumTrainsTimer_handler(object sender, EventArgs e)
        //private void TeensyTriggerKeithley()
        {
            --NumPulseTrains_cntdn;
            //Debug.WriteLine($"TimerHandler: cntdn:{NumPulseTrains_cntdn}");

            if (NumPulseTrains_cntdn <= 0)
            {
                //CMD_Done = true;
                CMD_Running = false;
                NumTrainsTimer.Stop();
            } else
                //Teensy.SendCommands(TeensyKeithleyTrigger_string);
                CEDTalker.SendString(TeensyKeithleyTrigger_string);

            UpdateRunningText();
        }

        private void SetKeithleyAmpl(double Ampl_uA)
        {
            K6221.SetAmplitude_uA(Ampl_uA);
        }

        private void coilCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (coilCheckBox.Checked)
            {
                sharedmem.ConnectSharedMemory();
            }
            else
            {
                sharedmem.DisconnectSharedMemory();
            }
        }
    }
}
