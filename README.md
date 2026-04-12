# cbr2cbz-converter-gui

A Linux desktop GUI utility for batch-converting CBR (RAR-archived) comic files to CBZ (ZIP-archived) format. Built with C# / Mono / GTK3.

## Screenshot

Drag CBR files onto the window, click **Start**, and the tool converts them in parallel while logging progress.

## Dependencies

```
sudo apt install mono-complete gtk-sharp3 unar unrar p7zip-full zip
```

> On Fedora/RHEL: `dnf install mono-complete gtk-sharp3 unar unrar zip`

## Build

```bash
mcs -pkg:gtk-sharp-3.0 -out:cbr2cbz.exe CbrToCbzConverter.cs
```

## Run

```bash
mono cbr2cbz.exe
```

## Adding files

- **Drag and drop** CBR files directly onto the window, **or**
- Click **Add Files…** to open a file chooser dialog (supports multi-select)

## Supported input formats

| Extension | Archive type | Notes |
|-----------|-------------|-------|
| .cbr      | RAR          | Primary path; validated via hex signature `52 61 72 21 1A 07` |
| .cbr      | ZIP          | Supported via the four-tool extraction chain: `unar` → `unrar` → `7z` → `unzip` |

Extracted images are re-packed as ZIP and saved as .cbz alongside the original. The original is moved to the system trash (recoverable via `gio trash`) only after successful page-count verification.

## Conversion status

Each file in the queue shows one of:

| Status | Meaning |
|--------|---------|
| Queued | Waiting to be processed |
| Converting | In progress |
| Done | Successfully converted |
| Failed | Conversion error (see log) |

## Known limitations

- Linux only (requires Mono runtime)
- Both RAR-based and ZIP-based CBR files are supported via the `unar` → `unrar` → `7z` → `unzip` fallback chain
- No built-in viewer — opens CBZ with your default file manager

## License

See [LICENSE](LICENSE).
