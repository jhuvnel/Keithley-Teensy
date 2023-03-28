// 2/20/2020 Dale - Made the Talker class more "bullet-proof". It gracefully handles
// connecting and disconnecting, and catches when Spike2 closes and sends a Disconnect
// packet. There should be very few circumstances now where it hangs or throws an exception.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Timers;

using System.Linq;
//using static CEDTalker.D;
using static KeithleyCrosspoint.D;

namespace CEDTalker
{
    // This is the data packet structure sent to and from Spike2 across the Named Pipe
    // after the Talker channel is opened. These must be declared using StructLayout(...)
    // because we send them as raw bytes across the Named Pipe to Spike2.
    //
    [StructLayout(LayoutKind.Sequential)]
    public class TalkPacket
    {
        public int nSize;  // packet size
        public int nCode;  // packet code/type
        public int nParam1;    // param/error code
        public int nParam2;    // second int param
        public double dParam1, dParam2; // two double params

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String szParam;

        public void Init()
        {
            // Note that this will correctly get the full size of the *derived*
            // object, just just the size of TalkerBase.
            nSize = Marshal.SizeOf(typeof(TalkPacket));
            nParam1 = 0;
            nParam2 = 0;
            dParam1 = 0;
            dParam2 = 0;
            //szParam[0] = 0;
            szParam = "";
        }

        public TalkPacket()
        {
            //printf("TalkBase() constructor called (our size is %d).\n", Marshal.SizeOf(this));
            Init();
        }
    }

    //
    // Packet to tell Spike2 about us.
    //
    [StructLayout(LayoutKind.Sequential)]
    public class TalkerInfo //: TalkBase
    {
        public int nSize;  // packet size
        public int nCode;  // packet code/type
        public int nChans; // Number of channels supported by us (this Talker)
        public int nVer;   // Talker interface version that we support (either 200 300)
        public double dParam1, dParam2; // two double params

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public String szDesc;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
        public String szName;
        public int nVerComp;
        public int nConfigID;
        public int nFlags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] nSpare = new int[32];

        public TalkerInfo() //(String Name, String Description)
        {
            nSize = Marshal.SizeOf(this);
        }
    }


    class CEDTalker
    {
        public NamedPipeClientStream PipeStream;
        SafeHandle PipeStreamHandle;
        byte[] PipeBuffer = null;
        GCHandle PipeBufferGCHandle;

        TalkPacket tp = new TalkPacket();
        TalkerInfo ti = new TalkerInfo();
        System.Timers.Timer PipeReadTimer;

        ~CEDTalker()
        {
            dprint("CEDTalker Destructor called.\n");
            Disconnect();
            //dprint("CEDTalker Destructor finished.\n");
        }

        public void AllocPipeBuffer(int size)
        {
            if (PipeBuffer != null)
                PipeBufferGCHandle.Free();
            PipeBuffer = new byte[size];
            PipeBufferGCHandle = GCHandle.Alloc(PipeBuffer, GCHandleType.Pinned);
        }

        // This is "definitive" since PeekPipe() uses Win32 PeekNamedPipe() to check for sure.
        public bool PipeIsConnected =>
            (PipeStream != null) && (PipeStream.IsConnected) && (PeekPipe() != null);

        public bool Connect()
        {
            dprint($"Connect() called on thread {Thread.CurrentThread.ManagedThreadId}\n");

            if (PipeIsConnected)
            {
                dprint("  -- Pipe already connected!\n");
                return true;  // Already successfully connected.
            }

            // First make sure any previous resources are released.
            Disconnect();

            // There are TWO steps here. First connect to the Spike2Talkers pipe.
            // We then close this, and re-connect to the pipe specific for our Talker.
            PipeStream = new NamedPipeClientStream("Spike2Talkers");
            dprint("Trying to connect...\n");

            try { PipeStream.Connect(100); }
            catch (TimeoutException e) { dprint("Spike2 NOT RUNNING!!\n"); return false; }

            dprint("Sending Connect request packet to Spike2Talkers pipe...\n");
            SendConnect();

            // Block and wait for reply from Spike2.
            dprint("Blocking and waiting for response from Spike2...\n");
            int NRead = ReadPacket();
            dprint($"Read {NRead} bytes, {PipeBuffer[0]},{PipeBuffer[1]},{PipeBuffer[2]},{PipeBuffer[3]}  {PipeBuffer[4]},{PipeBuffer[5]},{PipeBuffer[6]},{PipeBuffer[7]}  {PipeBuffer[8]},{PipeBuffer[9]},{PipeBuffer[10]},{PipeBuffer[11]}\n");
            PipeStream.Close();
            Thread.Sleep(50);

            dprint("Opening Talker-specific pipe...\n");
            // Now open the pipe specific to our talker.
            PipeStream = new NamedPipeClientStream("S2Talk_CSharpTalk");

            if (PipeStream == null)
            {
                dprint("Could not open Talker client pipe.\n");
                return false;
            }

            dprint("Connecting to S2Talk_CSharpTalk.\n");

            // We can *detect* the condition where Spike2 thinks we are already connected.
            // But there just isn't much we can *DO* about it, except to maybe alert the
            // user to please use the Spike2 menu to disconnect the CSharpTalker.
            try {PipeStream.Connect(100);}
            catch (TimeoutException e)
            {
                dprint("TimeoutException: Connect to our CSharpTalk named pipe has TIMED OUT!\n");

                return false;
            }
            catch (System.IO.IOException)
            {
                dprint("IOException: Spike2 already has open pipe to CSharpTalk!!\n");
                return false;
            }

            dprint("Connected!!\n");

            PipeStreamHandle = PipeStream.SafePipeHandle;

            // Periodically check if there is anything to read.
            PipeReadTimer = new System.Timers.Timer();
            PipeReadTimer.AutoReset = true;
            PipeReadTimer.Interval = 50;
            PipeReadTimer.Elapsed += PipeReadTimer_Elapsed;
            PipeReadTimer.Enabled = true;

            return true;
        }

        // Carefully check and release resources in reverse order. Force==true will try to
        // release everything, even if PipeStream is already null.
        public void Disconnect(bool Force=false)
        {
            dprint("Disconnect called.\n");
            if(!Force && (PipeStream == null))
            {
                dprint("  -- PipeStream already null.\n");
                return;
            }

            // This will first make sure the pipe is connected, so it is "safe".
            SendDisconnect();

            if (PipeReadTimer != null)
                PipeReadTimer.Enabled = false;
            PipeReadTimer = null;

            if(PipeStreamHandle != null)
                PipeStreamHandle.Dispose();
            PipeStreamHandle = null;

            if(PipeBuffer != null)
                PipeBufferGCHandle.Free();
            PipeBuffer = null;

            if (PipeStream != null)
            {
                PipeStream.Close();
                PipeStream.Dispose();
            }
            PipeStream = null;
        }

        // The Win32 PeekNamedPipe() function will authoritatively return failure if the
        // server connnection is closed. This is very valuable information that the C#
        // interface does not seem to otherwise be aware of.
        //
        // Returns null if the pipe is broken - so this call can be used directly as
        // a definitive test of pipe health.

        // First, the underlying Win32 call.
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PeekNamedPipe(SafeHandle handle,
            byte[] buffer, uint nBufferSize, ref uint bytesRead,
            ref uint bytesAvail, ref uint BytesLeftThisMessage);
        public uint? PeekPipe()
        {
            byte[] aPeekBuffer = new byte[1];
            uint aPeekedBytes = 0, aAvailBytes = 0, aLeftBytes = 0;

            if (PipeStreamHandle == null)
                return null;

            bool aPeekedSuccess = PeekNamedPipe(
                PipeStreamHandle, aPeekBuffer, 1,
                ref aPeekedBytes, ref aAvailBytes, ref aLeftBytes);

            // aPeekedSuccess will be FALSE if the pipe is broken (i.e., if the Server has
            // closed the pipe on its end).
            return aPeekedSuccess ? (uint?)aAvailBytes : null;
        }

        // This one simply returns 0, even if the pipe is broken/closed.
        public uint BytesAvailable()  {return PeekPipe() ?? 0;}

        // Return number of bytes read.
        public int ReadPacket()
        {
            if ((PipeStream == null) || (PipeBuffer == null))
                return 0;

            int NBytes = PipeStream.Read(PipeBuffer, 0, Marshal.SizeOf(typeof(TalkPacket)));
            dprint($"  ReadPacket() read {NBytes} bytes\n");
            Marshal.PtrToStructure(PipeBufferGCHandle.AddrOfPinnedObject(), tp);
            return NBytes;
        }

        // Need to be able to avoid checking connection during initial communication with
        // Spike2Talkers pipe during Connect(), hence the second "bool" argument.
        public void SendPacket(Object pkt, bool CheckConnection=true)
        {
            if (CheckConnection && !PipeIsConnected)
                return;

            int size = Marshal.SizeOf(pkt);

            if ((PipeBuffer == null) || (size > PipeBuffer.Length))
                AllocPipeBuffer(size);

            Marshal.StructureToPtr(pkt, PipeBufferGCHandle.AddrOfPinnedObject(), false);
            PipeStream.Write(PipeBuffer, 0, size);
        }

        // ==========================================================================
        // BELOW HERE, none of the functions should directly touch the pipe itself. We
        // keep all of the low-level pipe handling above, so it can be properly managed.

        private void PipeReadTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CheckForPackets();
        }

        // Send a Talker Connect request packet.
        void SendConnect()
        {
            tp.nSize = Marshal.SizeOf(typeof(TalkPacket));
            dprint($"tp.nSize: {tp.nSize}\n");
            tp.nCode = 0; // Connect
            tp.nParam1 = 2; // Talker version number
            tp.szParam = "CSharpTalk";

            SendPacket(tp, false); // Do NOT check pipe connection status first.
        }

        public void SendDisconnect()
        {
            // We are requesting to quit, so send Spike2 a Close packet.
            tp.Init();
            tp.nCode = 14; // close
            SendPacket(tp);
        }

        void SendTalkerInfo()
        {
            ti.nSize = Marshal.SizeOf(typeof(TalkerInfo));
            ti.nCode = 1 | 0x40000000;
            ti.nChans = 0;
            ti.nVer = 200;
            ti.szName = "CSharpTalk";
            ti.szDesc = "This is my C# Talker! Isn't that cool??!!";
            ti.nVerComp = 200;
            ti.nFlags = 0x10;   // TKF_ACTALL - notify of start/stop sampling,
                                // even if we have no channels.
            SendPacket(ti);
        }

        public void SendCmd(String str)
        {
            dprint($"    SendCmd({str})  sending...   \n");
            tp.nSize = Marshal.SizeOf(typeof(TalkPacket));
            tp.nCode = 23;
            tp.nParam1 = 0;
            tp.szParam = str;
            SendPacket(tp);
            dprint("DONE Sending.\n");
        }

        public void SendString(String str)
        {
            dprint($"CED Sending String: '{str}'\n");
            tp.nSize = Marshal.SizeOf(typeof(TalkPacket));
            tp.nCode = 22;
            tp.nParam1 = 0;
            tp.szParam = str;
            SendPacket(tp);
        }

        //public void 

        public void CheckForPackets()
        {
            while (BytesAvailable() > 0)
            {
                ReadPacket();
                HandlePacket();
            }
        }

        public void HandlePacket()
        {
            //int PktCode = PipeBuffer[4];
            int PktCode = tp.nCode;
            dprint($"    In HandlePacket(). Code {PktCode}, 0x{tp.nParam1:X}\n");

            switch (PktCode)
            {
                case 1: // GetInfo request from Spike2.
                    dprint($"Received GetInfo request (Spike2 version {tp.nParam1}). Sending TalkerInfo packet.\n");
                    SendTalkerInfo();
                    break;
                case 10:
                    dprint("Got 'SampleClear' from Spike2.\n");
                    break;
                case 13:
                    dprint("Got 'SampleStart' from Spike2.\n");
                    break;
                case 14:
                    dprint("Got 'TalkerClose' from Spike2.\n");
                    Disconnect();
                    break;
                case 15:
                    dprint("Got 'SampleStop' from Spike2.\n");
                    break;
                case 22:
                    dprint($"Got SendString: '{tp.szParam}'\n");
                    break;
                default:
                    dprint($"Code {PktCode}  NOT HANDLED!!\n");
                    break;
            }
        }

        // These functions are called by the CMDProcessor for the scripting language. We
        // include the CED...() preface with these method names, to highlight that these
        // particular functions are called from outside the class by the Command
        // Processor scripts.
        string CEDFileBasePath = "";
        public void CEDConnect() { Connect(); }
        //public void CEDSetPath(string Path) { CEDFileBasePath = Path; }
        public void CEDSetPath(string[] tokens)
        {
            CEDFileBasePath = String.Join(" ", tokens.Skip(1));
            dprint($"Got path: '{CEDFileBasePath}'\n");
        }
        public void CEDConfigLoad(string FileName) { SendCmd($"configload|{FileName}"); }
        public void CEDOpen() { SendCmd("samplestart"); }
        public void CEDCloseAndSave(string FTextBox, string FName)
        {
            if (!string.IsNullOrEmpty(CEDFileBasePath))
                CEDFileBasePath = CEDFileBasePath.TrimEnd('\\') + "\\";

            string FullName = $"{CEDFileBasePath}{DateTime.Now.ToString("yyyy_MMdd_hhmmss")}_{FTextBox}_{FName}.smr";
            SendCmd($"samplestop|{FullName}|close");
        }

        // Pass in the full path and file name for a Spike2 script to run.
        public void CEDRunScript(string[] tokens) {SendCmd($"ScriptRun|{String.Join(" ", tokens.Skip(1))}");}
        public void CEDSendString(string[] tokens) { SendString(String.Join(" ", tokens.Skip(1))); }


        // For testing things.
        int filecount = 0;
        public void DoKeysForTesting(string Key)
        {
            dprint($"CEDTalker got keypress: '{Key}'\n");
            switch (Key)
            {
                case "q":
                    //goto QUIT_LOOP;
                    break;

                case "!": SendCmd("samplestart"); break;
                case "1":
                    SendCmd($"samplestop|c:\\downloads\\DataFile_{++filecount:d03}.smr|close");
                    break;
                case "W": SendCmd("samplewrite|on"); break;
                case "w": SendCmd("samplewrite|off"); break;
                case "2": SendCmd("sampleabort"); break;
                case "8": SendString("A string from C# to Spike2."); break;
                case "9": SendCmd("ScriptRun|c:\\downloads\\daletest.s2s"); break;
                case "0": SendCmd("ScriptRun|c:\\downloads\\daletest2.s2s"); break;

                case "d": Disconnect(); break;
            }
        }
    }
}
