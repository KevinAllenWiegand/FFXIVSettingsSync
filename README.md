# FFXIVSettingsSync
Application to help keep FFXIV settings synchronized across multiple windows devices.

This requires the use of Dropbox for the "remote" copy.  The application watches your local settings (from My Documents) and your remote settings (in Dropbox), and keeps them in sync, making sure that the latest copy is always in both places.  It uses an on-the-fly calculated file hash to see if file contents are different when a file is changed either locally, or in Dropbox.  If files are different, it uses the Last Modified Date to determine which file is newer, and copies the newer file over the older one.

Only cfg and dat files are processed.  Of those file types, FFXIF.cfg, and FFXIV_BOOT.cfg files are ignored, since those appear to be system-specific configurations.  There are some folders that are ignored entirely as well, such as logs, downloads, and screenshots.

The local "My Documents" FFXIV settings folder is determined by looking at the HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\SquareEnix registry key.
The Dropbox location is determined by looking at the info.json file located at %LocalAppData%\Dropbox.  Settings are then stored under a "FFXIV\Settings Backup" folder.

No more manually copying files!

This is a .NET WPF application.  It needs to be ran manually anytime you reboot your computer.  It sits in the system tray, and has a very minimal UI.

Yes, the code is a bit of a monolith in the main service, but I threw this together in just a few hours.  I might fix it up later, but for now, it does what it needs to do.
