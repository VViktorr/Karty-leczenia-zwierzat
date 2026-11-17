using System.Runtime.InteropServices;

namespace KartyLeczenia.Windows;

internal static class PdfMail
{
    const uint MapiLogonUi = 0x00000001;
    const uint MapiDialog = 0x00000008;
    const uint MapiUserAbort = 1;

    internal static void Compose(nint owner, string path, string fileName)
    {
        var file = new MapiFile
        {
            Position = uint.MaxValue,
            Path = path,
            FileName = fileName
        };
        nint attachment = Marshal.AllocHGlobal(Marshal.SizeOf<MapiFile>());
        bool initialized = false;
        try
        {
            Marshal.StructureToPtr(file, attachment, false);
            initialized = true;
            var message = new MapiMessage
            {
                Subject = "Karta leczenia zwierząt",
                Body = "Dzień dobry,\r\n\r\nw załączeniu karta leczenia zwierząt.",
                FileCount = 1,
                Files = attachment
            };
            uint result = SendMail(0, owner, ref message, MapiLogonUi | MapiDialog, 0);
            if (result is not (0 or MapiUserAbort))
                throw new InvalidOperationException(
                    $"Nie udało się otworzyć wiadomości z załącznikiem (kod MAPI {result}). Zapisz PDF i dołącz go w swojej poczcie.");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            throw new InvalidOperationException(
                "Program pocztowy nie udostępnia obsługi MAPI. Zapisz PDF i dołącz go w swojej poczcie.", ex);
        }
        finally
        {
            if (initialized) Marshal.DestroyStructure<MapiFile>(attachment);
            Marshal.FreeHGlobal(attachment);
        }
    }

    [DllImport("mapi32.dll", EntryPoint = "MAPISendMailW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint SendMail(nint session, nint owner, ref MapiMessage message, uint flags, uint reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MapiMessage
    {
        public uint Reserved;
        public string? Subject;
        public string? Body;
        public string? MessageType;
        public string? DateReceived;
        public string? ConversationId;
        public uint Flags;
        public nint Originator;
        public uint RecipientCount;
        public nint Recipients;
        public uint FileCount;
        public nint Files;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MapiFile
    {
        public uint Reserved;
        public uint Flags;
        public uint Position;
        public string Path;
        public string FileName;
        public nint FileType;
    }
}
