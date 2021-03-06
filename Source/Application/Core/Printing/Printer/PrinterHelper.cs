﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace pdfforge.PDFCreator.Core.Printing.Printer
{
    public interface IPrinterProvider
    {
        /// <summary>
        ///     Get all printers that are attached to the pdfcmon port
        /// </summary>
        /// <returns>A list of printers attached to the port</returns>
        IList<string> GetPDFCreatorPrinters();
    }

    public interface IPrinterHelper : IPrinterProvider
    {
        /// <summary>
        ///     Get a list of printers attached to the given port
        /// </summary>
        /// <param name="portName">Name of the printer port</param>
        /// <returns>A list of printers attached to the port</returns>
        IList<string> GetPrinters(string portName);

        string GetApplicablePDFCreatorPrinter(string requestedPrinter);

        string GetApplicablePDFCreatorPrinter(string requestedPrinter, string defaultPrinter);

        void PrintWindowsTestPage(string printerName);

        string GetDefaultPrinter();

        bool SetDefaultPrinter(string printerName);

        bool IsValidPrinterName(string printerName);
    }

    public class PrinterHelper : IPrinterHelper
    {
        // ReSharper disable once InconsistentNaming
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        public IList<string> GetPrinters(string portName)
        {
            try
            {
                var printerInfos = EnumPrinters(PrinterEnumFlags.PRINTER_ENUM_LOCAL);

                var printers = new List<string>();

                foreach (var printer in printerInfos)
                {
                    if (printer.pPortName.Equals(portName, StringComparison.OrdinalIgnoreCase))
                        printers.Add(printer.pPrinterName);
                }

                printers.Sort();

                return printers;
            }
            catch (Win32Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>
        ///     List all printers that are connected to the pdfcmon port
        /// </summary>
        /// <returns>A Collection of PDFCreator printers</returns>
        public IList<string> GetPDFCreatorPrinters()
        {
            return GetPrinters("pdfcmon");
        }

        /// <summary>
        ///     Prints a windows test page to the printer with the given name
        /// </summary>
        /// <param name="printerName">Name of the printer to use</param>
        public void PrintWindowsTestPage(string printerName)
        {
            var settings = new PrinterSettings();
            var defaultPrinter = settings.PrinterName;
            var printer = GetApplicablePDFCreatorPrinter(printerName, defaultPrinter);

            var psi = new ProcessStartInfo("RUNDLL32.exe", "PRINTUI.DLL,PrintUIEntry /k /n \"" + printer + "\"");
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }

        /// <summary>
        ///     Function to check if the printer name is already used by another installed printer
        /// </summary>
        /// <param name="printerName">proposed name of printer</param>
        /// <returns>true if printerName is unique or false if the name is already in use</returns>
        public bool IsValidPrinterName(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return false;

            foreach (string installedPrinter in PrinterSettings.InstalledPrinters)
            {
                if (installedPrinter.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        ///     Function that searches all printers connected to the PDFCreator port and returns the most applicable. This is
        ///     either the requested printer,
        ///     the default Printer, the one with the name "PDFCreator" or the first one in alphabetical order.
        /// </summary>
        /// <param name="requestedPrinter">Name of the primary PDFCreator printer</param>
        /// <param name="defaultPrinter">Name of the current default printer</param>
        /// <returns>null if no PDFCreator printer is installed, else the name of the most applicable PDFCreator printer</returns>
        public string GetApplicablePDFCreatorPrinter(string requestedPrinter, string defaultPrinter)
        {
            var printers = GetPDFCreatorPrinters();

            if (printers.Count == 0)
                return null;

            // ReSharper disable once InconsistentNaming
            string PDFCreatorPrinter = null;
            var isDefaultPrinter = false;

            foreach (var printer in printers)
            {
                if (PDFCreatorPrinter == null)
                    PDFCreatorPrinter = printer;

                if (!isDefaultPrinter)
                    if (printer.Equals("PDFCreator", StringComparison.OrdinalIgnoreCase))
                        PDFCreatorPrinter = printer;

                if (printer.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase))
                {
                    PDFCreatorPrinter = printer;
                    isDefaultPrinter = true;
                }

                if (printer.Equals(requestedPrinter, StringComparison.OrdinalIgnoreCase))
                    return printer;
            }

            return PDFCreatorPrinter;
        }

        public string GetApplicablePDFCreatorPrinter(string requestedPrinter)
        {
            return GetApplicablePDFCreatorPrinter(requestedPrinter, GetDefaultPrinter());
        }

        public string GetDefaultPrinter()
        {
            return new PrinterSettings().PrinterName;
        }

        public bool SetDefaultPrinter(string printerName)
        {
            return SetDefaultPrinterWin32(printerName);
        }

        private IEnumerable<PRINTER_INFO_5> EnumPrinters(PrinterEnumFlags flags)
        {
            // level of the EnumPrinters call. Level 5 returns a PRINTER_INFO_5 structure
            uint enumLevel = 5;

            uint cbNeeded = 0;
            uint cReturned = 0;
            if (EnumPrinters(flags, null, enumLevel, IntPtr.Zero, 0, ref cbNeeded, ref cReturned))
            {
                return new PRINTER_INFO_5[0];
            }

            var lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error != ERROR_INSUFFICIENT_BUFFER)
                throw new Win32Exception(lastWin32Error);

            var pAddr = Marshal.AllocHGlobal((int)cbNeeded);
            if (EnumPrinters(flags, null, enumLevel, pAddr, cbNeeded, ref cbNeeded, ref cReturned))
            {
                var printerInfo = new PRINTER_INFO_5[cReturned];
                var offset = pAddr;
                var type = typeof(PRINTER_INFO_5);
                var increment = Marshal.SizeOf(type);
                for (var i = 0; i < cReturned; i++)
                {
                    printerInfo[i] = (PRINTER_INFO_5)Marshal.PtrToStructure(offset, type);
                    offset += increment;
                }
                Marshal.FreeHGlobal(pAddr);
                return printerInfo;
            }
            lastWin32Error = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastWin32Error);
        }

        #region Windows Spooler

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool EnumPrinters(PrinterEnumFlags flags, string name, uint level, IntPtr pPrinterEnum, uint cbBuf, ref uint pcbNeeded, ref uint pcReturned);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "SetDefaultPrinter")]
        private static extern bool SetDefaultPrinterWin32(string printerName);

        // ReSharper disable once InconsistentNaming
        // ReSharper disable FieldCanBeMadeReadOnly.Local
        // ReSharper disable MemberCanBePrivate.Local
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PRINTER_INFO_5
        {
            [MarshalAs(UnmanagedType.LPTStr)] public string pPrinterName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pPortName;
            public uint Attributes;
            public uint DeviceNotSelectedTimeout;
            public uint TransmissionRetryTimeout;
        }

        // ReSharper restore FieldCanBeMadeReadOnly.Local
        // ReSharper restore MemberCanBePrivate.Local
        [Flags]
        private enum PrinterEnumFlags
        {
            // ReSharper disable UnusedMember.Local
            // ReSharper disable InconsistentNaming
            PRINTER_ENUM_DEFAULT = 0x00000001,

            PRINTER_ENUM_LOCAL = 0x00000002,
            PRINTER_ENUM_CONNECTIONS = 0x00000004,
            PRINTER_ENUM_FAVORITE = 0x00000004,
            PRINTER_ENUM_NAME = 0x00000008,
            PRINTER_ENUM_REMOTE = 0x00000010,
            PRINTER_ENUM_SHARED = 0x00000020,
            PRINTER_ENUM_NETWORK = 0x00000040,
            PRINTER_ENUM_EXPAND = 0x00004000,
            PRINTER_ENUM_CONTAINER = 0x00008000,
            PRINTER_ENUM_ICONMASK = 0x00ff0000,
            PRINTER_ENUM_ICON1 = 0x00010000,
            PRINTER_ENUM_ICON2 = 0x00020000,
            PRINTER_ENUM_ICON3 = 0x00040000,
            PRINTER_ENUM_ICON4 = 0x00080000,
            PRINTER_ENUM_ICON5 = 0x00100000,
            PRINTER_ENUM_ICON6 = 0x00200000,
            PRINTER_ENUM_ICON7 = 0x00400000,
            PRINTER_ENUM_ICON8 = 0x00800000,
            PRINTER_ENUM_HIDE = 0x01000000
            // ReSharper restore UnusedMember.Local
            // ReSharper restore InconsistentNaming
        }

        #endregion Windows Spooler
    }
}
