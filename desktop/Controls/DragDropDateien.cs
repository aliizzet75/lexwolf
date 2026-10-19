using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using LexWolf.Services;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace LexWolf.Controls;

/// <summary>
/// Dateien aus einem Drag-and-drop holen: aus dem Explorer (normale Pfade) und aus Outlook
/// (virtuelle Dateien: Mails als .msg, Anhänge als Datei). Outlook liefert nur Namen
/// (FileGroupDescriptorW) und Inhalte (FileContents) — die werden in einen Zwischenordner geschrieben.
/// Muss im Drop-Ereignis aufgerufen werden; danach sind die Outlook-Daten nicht mehr verfügbar.
/// </summary>
public static class DragDropDateien
{
    private const string Deskriptor = "FileGroupDescriptorW";
    private const string DeskriptorAnsi = "FileGroupDescriptor";

    public static bool EnthaeltDateien(System.Windows.IDataObject daten) =>
        daten.GetDataPresent(DataFormats.FileDrop) || daten.GetDataPresent(Deskriptor) || daten.GetDataPresent(DeskriptorAnsi);

    public static List<string> Holen(System.Windows.IDataObject daten, string zwischenordner)
    {
        if (daten.GetDataPresent(DataFormats.FileDrop) && daten.GetData(DataFormats.FileDrop) is string[] pfade)
            return pfade.SelectMany(p => Directory.Exists(p)
                    ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                    : File.Exists(p) ? new[] { p } : Array.Empty<string>())
                .ToList();

        var namen = Dateinamen(daten);
        if (namen.Count == 0 || daten is not ComDataObject com) return new List<string>();
        Directory.CreateDirectory(zwischenordner);

        var ergebnis = new List<string>();
        var format = (short)DataFormats.GetDataFormat("FileContents").Id;
        for (var i = 0; i < namen.Count; i++)
        {
            var fe = new FORMATETC
            {
                cfFormat = format, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = i, ptd = IntPtr.Zero,
                tymed = TYMED.TYMED_ISTORAGE | TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
            };
            com.GetData(ref fe, out var medium);
            try
            {
                var ziel = DokumentExport.FreierPfad(zwischenordner, SichererName(namen[i]));
                switch (medium.tymed)
                {
                    case TYMED.TYMED_ISTORAGE: SpeichereStorage(medium.unionmember, ziel); break;
                    case TYMED.TYMED_ISTREAM: SpeichereStream(medium.unionmember, ziel); break;
                    case TYMED.TYMED_HGLOBAL: SpeichereHGlobal(medium.unionmember, ziel); break;
                    default: continue;
                }
                ergebnis.Add(ziel);
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        return ergebnis;
    }

    /// <summary>Dateinamen aus FILEGROUPDESCRIPTOR(W): Anzahl, dann je Eintrag 72 Byte Kopf + Name
    /// (260 Zeichen; Unicode 592 Byte je Eintrag, ANSI 332 Byte).</summary>
    private static List<string> Dateinamen(System.Windows.IDataObject daten)
    {
        var unicode = daten.GetDataPresent(Deskriptor);
        if (daten.GetData(unicode ? Deskriptor : DeskriptorAnsi) is not MemoryStream ms) return new List<string>();
        var b = ms.ToArray();
        if (b.Length < 4) return new List<string>();
        var anzahl = BitConverter.ToInt32(b, 0);
        var groesse = unicode ? 592 : 332;
        var namen = new List<string>();
        for (var i = 0; i < anzahl && 4 + (i + 1) * groesse <= b.Length; i++)
        {
            var start = 4 + i * groesse + 72;
            var name = unicode
                ? Encoding.Unicode.GetString(b, start, 520)
                : Encoding.Default.GetString(b, start, 260);
            namen.Add(name.Split('\0')[0]);
        }
        return namen;
    }

    private static string SichererName(string name)
    {
        var s = string.Concat(Path.GetFileName(name).Select(c => "\\/:*?\"<>|".Contains(c) || char.IsControl(c) ? '_' : c)).Trim();
        return s.Length == 0 ? "Datei" : s;
    }

    private static void SpeichereStorage(IntPtr zeiger, string ziel)
    {
        var quelle = (IStorage)Marshal.GetObjectForIUnknown(zeiger);
        IStorage? neu = null;
        try
        {
            Marshal.ThrowExceptionForHR(StgCreateDocfile(ziel, StgmCreate | StgmReadWrite | StgmShareExclusive, 0, out neu));
            quelle.CopyTo(0, null, IntPtr.Zero, neu);
            neu.Commit(0);
        }
        finally
        {
            if (neu is not null) Marshal.ReleaseComObject(neu);
            Marshal.ReleaseComObject(quelle);
        }
    }

    private static void SpeichereStream(IntPtr zeiger, string ziel)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(zeiger);
        try
        {
            stream.Stat(out var stat, 1 /* STATFLAG_NONAME */);
            using var fs = File.Create(ziel);
            var puffer = new byte[64 * 1024];
            var rest = stat.cbSize;
            var gelesenPtr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                while (rest > 0)
                {
                    stream.Read(puffer, (int)Math.Min(puffer.Length, rest), gelesenPtr);
                    var gelesen = Marshal.ReadInt32(gelesenPtr);
                    if (gelesen <= 0) break;
                    fs.Write(puffer, 0, gelesen);
                    rest -= gelesen;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(gelesenPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    private static void SpeichereHGlobal(IntPtr handle, string ziel)
    {
        var groesse = (int)GlobalSize(handle);
        var zeiger = GlobalLock(handle);
        try
        {
            var daten = new byte[groesse];
            Marshal.Copy(zeiger, daten, 0, groesse);
            File.WriteAllBytes(ziel, daten);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private const uint StgmCreate = 0x1000, StgmReadWrite = 0x2, StgmShareExclusive = 0x10;

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgCreateDocfile(string pwcsName, uint grfMode, uint reserved, out IStorage ppstgOpen);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    // Nur die Methoden bis Commit — die Reihenfolge muss der COM-Vtable entsprechen.
    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, uint grfMode, uint reserved1, uint reserved2, out IStream ppstm);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IntPtr reserved1, uint grfMode, uint reserved2, out IStream ppstm);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, uint grfMode, uint reserved1, uint reserved2, out IStorage ppstg);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage pstgPriority, uint grfMode, IntPtr snbExclude, uint reserved, out IStorage ppstg);
        void CopyTo(uint ciidExclude, [MarshalAs(UnmanagedType.LPArray)] Guid[]? rgiidExclude, IntPtr snbExclude, IStorage pstgDest);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage pstgDest, [MarshalAs(UnmanagedType.LPWStr)] string pwcsNewName, uint grfFlags);
        void Commit(uint grfCommitFlags);
    }
}
