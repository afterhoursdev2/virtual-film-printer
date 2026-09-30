# Virtual Film Printer

A test printer for AMSI Printer. It shows in Windows as a normal printer with the film sizes as paper
(8INx10IN … 14INx17IN, 13INx17IN), and instead of using film it keeps every sheet it is sent as a picture,
so a whole modality → AMSI Printer → printer run can be checked without wasting film. Sheets that are an exact
repeat of an earlier one are marked in red: that is what a duplicate-print bug looks like.

## How it works

- `scripts\Install-Printer.ps1` adds a Windows printer on the XPS driver that ships with Windows, the film sizes
  as Windows paper forms, and a Standard TCP/IP port that sends each print job to `127.0.0.1:9100`.
- `VirtualFilmPrinter.exe` listens on that port. One connection is one Windows print job. Each job is kept
  as it arrived, converted from OpenXPS (what the driver sends over a port) to XPS, and drawn to a PNG per sheet
  at 100 dpi on white. The document name comes from the Windows print queue.

## Use

1. Once, from an elevated PowerShell: `powershell -ExecutionPolicy Bypass -File scripts\Install-Printer.ps1`
2. Start `VirtualFilmPrinter.exe` (`--port N` for another port, matching `Install-Printer.ps1 -Port N`).
3. In AMSI Printer, choose **Virtual Film Printer** as a rule's printer and map the film sizes on the Paper tab
   to the matching `…IN` papers.
4. Print from the modality. Each sheet appears in the list; double-click to open its PNG.

Jobs are kept in `Documents\VirtualFilmPrinter\Jobs\<date>\<number>_<time>\`: `received.oxps` (as sent),
`converted.xps`, `page-N.png`, `job.txt`. **Clear list** starts a fresh comparison; the files stay.

To remove: `scripts\Install-Printer.ps1 -Remove`. The paper sizes are kept while another printer on a `VFP_`
port remains.

## Build and test

```
dotnet build VirtualFilmPrinter.csproj
dotnet test tests\VirtualFilmPrinter.Tests
```

The queue test prints through any printer whose port is `VFP_127.0.0.1_9100`, so the printer must be installed
and the app must not be running (the test listens on 9100 itself).
