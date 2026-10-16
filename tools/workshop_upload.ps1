# Updates Workshop item 3809174815 (GK2 Move Buildings): content, description, change note.
# Title, tags, visibility and preview are left as they are on Steam.
# Expects next to it: steam_api64.dll (from the game), steam_appid.txt (4358690), content\, description.txt, changenote.txt.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
# DllImport and steam_appid.txt use the process directory, which Set-Location does not change.
[Environment]::CurrentDirectory = $here
$env:SteamAppId = '4358690'
$env:SteamGameId = '4358690'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class S {
    const string D = "steam_api64.dll";
    [DllImport(D)] public static extern int SteamAPI_InitFlat(byte[] err);
    [DllImport(D)] public static extern void SteamAPI_Shutdown();
    [DllImport(D)] public static extern void SteamAPI_RunCallbacks();
    [DllImport(D)] public static extern IntPtr SteamAPI_SteamUGC_v021();
    [DllImport(D)] public static extern IntPtr SteamAPI_SteamUtils_v010();
    [DllImport(D)] public static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr u);
    [DllImport(D)] public static extern ulong SteamAPI_ISteamUGC_StartItemUpdate(IntPtr ugc, uint app, ulong id);
    [DllImport(D)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUGC_SetItemContent(IntPtr ugc, ulong h, byte[] folder);
    [DllImport(D)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUGC_SetItemDescription(IntPtr ugc, ulong h, byte[] desc);
    [DllImport(D)] public static extern ulong SteamAPI_ISteamUGC_SubmitItemUpdate(IntPtr ugc, ulong h, byte[] note);
    [DllImport(D)] public static extern int SteamAPI_ISteamUGC_GetItemUpdateProgress(IntPtr ugc, ulong h, out ulong done, out ulong total);
    [DllImport(D)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr u, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [DllImport(D)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUtils_GetAPICallResult(IntPtr u, ulong call, byte[] buf, int cb, int cbk, [MarshalAs(UnmanagedType.I1)] out bool failed);
    public static byte[] Z(string s) { byte[] b = System.Text.Encoding.UTF8.GetBytes(s); byte[] r = new byte[b.Length + 1]; Array.Copy(b, r, b.Length); return r; }
}
'@

$itemId = [uint64]3809174815
$content = Join-Path $here 'content'
$desc = [IO.File]::ReadAllText((Join-Path $here 'description.txt'), [Text.Encoding]::UTF8)
$note = [IO.File]::ReadAllText((Join-Path $here 'changenote.txt'), [Text.Encoding]::UTF8)

$err = New-Object byte[] 1024
$init = [S]::SteamAPI_InitFlat($err)
if ($init -ne 0) { throw "Steam init failed ($init): " + [Text.Encoding]::UTF8.GetString($err).TrimEnd([char]0) }
try {
    $ugc = [S]::SteamAPI_SteamUGC_v021()
    $utils = [S]::SteamAPI_SteamUtils_v010()
    $app = [S]::SteamAPI_ISteamUtils_GetAppID($utils)
    Write-Host "App $app, item $itemId, content $content"
    if ($app -ne 4358690) { throw "Wrong app id $app" }
    $h = [S]::SteamAPI_ISteamUGC_StartItemUpdate($ugc, $app, $itemId)
    if ($h -eq [uint64]::MaxValue) { throw 'StartItemUpdate returned an invalid handle' }
    if (-not [S]::SteamAPI_ISteamUGC_SetItemContent($ugc, $h, [S]::Z($content))) { throw 'SetItemContent failed' }
    if (-not [S]::SteamAPI_ISteamUGC_SetItemDescription($ugc, $h, [S]::Z($desc))) { throw 'SetItemDescription failed' }
    $call = [S]::SteamAPI_ISteamUGC_SubmitItemUpdate($ugc, $h, [S]::Z($note))
    Write-Host 'Submitting...'
    $failed = $false
    $last = ''
    for ($i = 0; $i -lt 1200; $i++) {
        [S]::SteamAPI_RunCallbacks()
        if ([S]::SteamAPI_ISteamUtils_IsAPICallCompleted($utils, $call, [ref]$failed)) { break }
        $d = [uint64]0; $t = [uint64]0
        $st = [S]::SteamAPI_ISteamUGC_GetItemUpdateProgress($ugc, $h, [ref]$d, [ref]$t)
        $line = "status $st ($d/$t)"
        if ($line -ne $last) { Write-Host $line; $last = $line }
        Start-Sleep -Milliseconds 250
    }
    # SubmitItemUpdateResult_t (callback 3404): EResult, bool legal, PublishedFileId_t
    $buf = New-Object byte[] 16
    if (-not [S]::SteamAPI_ISteamUtils_GetAPICallResult($utils, $call, $buf, 16, 3404, [ref]$failed) -or $failed) { throw 'No result from SubmitItemUpdate (IO failure or timeout)' }
    $result = [BitConverter]::ToInt32($buf, 0)
    $legal = $buf[4] -ne 0
    Write-Host "Result $result (1 = OK), legal agreement needed: $legal"
    if ($result -ne 1) { exit 1 }
    Write-Host 'Upload complete: https://steamcommunity.com/sharedfiles/filedetails/?id=3809174815'
}
finally {
    [S]::SteamAPI_Shutdown()
}
