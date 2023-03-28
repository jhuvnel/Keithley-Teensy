using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.MemoryMappedFiles;

enum shmem_CMD { NO_CMD, OPEN_FILE, CLOSE_FILE, START_COLLECTING, STOP_COLLECTING };

// This is the memory structure that is shared with the C++ program.
// This MUST EXACTLY match the structure layout in the C++ program.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Showall_shmem
{
    public int CMD;
    public fixed byte FileName[1000];
    public int GUI_isalive;
    public int showall_isalive;
}

unsafe delegate void MemCpyImpl(byte* src, byte* dest, int len);

namespace KeithleyCrosspoint
{
    unsafe class SharedMemory
    {
        MemoryMappedFile memfile;
        MemoryMappedViewAccessor shmem_accessor;
        public Showall_shmem shmem = new Showall_shmem();
        public Showall_shmem* shmempt = null;

        public bool ReadShmem()
        {
            if (shmem_accessor == null)
                return false;
            shmem_accessor.Read<Showall_shmem>(0, out shmem);
            return true;
        }

        public void WriteShmem()
        {
            if (shmem_accessor == null)
                return;
            shmem_accessor.Write<Showall_shmem>(0, ref shmem);
        }

        public void ConnectSharedMemory()
        {
            memfile = MemoryMappedFile.CreateOrOpen("SHOWALL_coil_MappingObject",
                                       Marshal.SizeOf(typeof(Showall_shmem)),
                                       MemoryMappedFileAccess.ReadWrite
                                       );
            shmem_accessor = memfile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            ReadShmem();
            shmem.CMD = (int)shmem_CMD.NO_CMD;
            shmem.GUI_isalive = 1;
            WriteShmem();

            byte* pt = (byte*)0;
            shmem_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pt);
            shmempt = (Showall_shmem*)pt;
        }

        // Wait for showall program to finish processing command.
        public bool WaitCmdShmem()
        {
            int i;

            if (!ReadShmem() || (shmem.showall_isalive == 0))
                return false;

            for(i=0; (i<50) && (shmem.CMD != (int)shmem_CMD.NO_CMD); ++i)
            {
                System.Threading.Thread.Sleep(5);
                ReadShmem();
            }

            if (i == 50)
                return false;

            return true;
        }

        public void DisconnectSharedMemory()
        {
            ReadShmem();
            shmem.GUI_isalive = 0;
            shmem.CMD = (int)shmem_CMD.NO_CMD;
            WriteShmem();
            shmem_accessor = null;
            memfile = null;
        }

        public bool OpenFile(string fname)
        {
            if (!WaitCmdShmem())
                return false;

            // Copy file name. Be sure to add 0 termination character.
            fixed (byte* p = shmem.FileName)
            {
                Marshal.Copy(Encoding.ASCII.GetBytes(fname), 0, (IntPtr)p, fname.Length);
                p[fname.Length] = 0;
            }

            shmem.CMD = (int)shmem_CMD.OPEN_FILE;
            WriteShmem();
            // This works. Can switch to using direct pointer access instead of read/write.
//            shmempt->CMD = (int)shmem_CMD.OPEN_FILE;
            return true;
        }

        public bool CloseFile()
        {
            if (!WaitCmdShmem())
                return false;

            shmem.CMD = (int)shmem_CMD.CLOSE_FILE;
            WriteShmem();
            return true;
        }

        public bool StartCollecting()
        {
            if (!WaitCmdShmem())
                return false;

            shmem.CMD = (int)shmem_CMD.START_COLLECTING;
            WriteShmem();
            return true;
        }

        public bool StopCollecting()
        {
            if (!WaitCmdShmem())
                return false;

            shmem.CMD = (int)shmem_CMD.STOP_COLLECTING;
            WriteShmem();
            return true;
        }

    }
}


/**************************************
public class TestMain
{
    static bool kbhit() {return Console.KeyAvailable;}
    static char getch() {return Console.ReadKey(true).KeyChar;}
    static void print(string format, params object[] args) {Console.Write(format,args);}



    static public unsafe void Main()
    {
        MoogGUI.SharedMemory shm = new MoogGUI.SharedMemory();

        shm.ConnectSharedMemory();

        print("Test. Does this work?\n");
        print("Sizeof(showall_shmem) {0}\n", Marshal.SizeOf(typeof(showall_shmem)));

        while (true)
        {
            if (kbhit()) switch (getch()) {
                case 'O': shm.OpenFile("TestGUIFile123"); break;
                case 'C': shm.StartCollecting(); break;
                case 'c': shm.StopCollecting(); break;
                case 'o': shm.CloseFile(); break;

                case 'q':
                case (char)27:
                    goto QUIT;
            }
            shm.ReadShmem();
            print("showall counter {0}   {1}\r", shm.shmem.showall_isalive, shm.shmempt->showall_isalive);
            System.Threading.Thread.Sleep(50);
        }

QUIT:
        shm.DisconnectSharedMemory();
    }
}
**************************************/
