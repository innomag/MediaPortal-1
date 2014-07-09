#region Copyright (C) 2005-2011 Team MediaPortal

// Copyright (C) 2005-2011 Team MediaPortal
// http://www.team-mediaportal.com
// 
// MediaPortal is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MediaPortal is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MediaPortal. If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace Mediaportal.TV.Server.TVLibrary.Interfaces
{
  /// <summary>
  /// This class handles HRESULT codes returned by COM classes and assemblies.
  /// </summary>
  /// <remarks>
  /// An HRESULT code is a 32 bit value laid out as follows:
  ///
  ///   3 3 2 2 2 2 2 2 2 2 2 2 1 1 1 1 1 1 1 1 1 1
  ///   1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0
  ///  +---+-+-+-----------------------+-------------------------------+
  ///  |Sev|C|R|     Facility          |               Code            |
  ///  +---+-+-+-----------------------+-------------------------------+
  ///
  ///  where
  ///
  ///      Sev - is the severity code
  ///
  ///          00 - Success
  ///          01 - Informational
  ///          10 - Warning
  ///          11 - Error
  ///
  ///      C - is the Customer code flag
  ///
  ///      R - is a reserved bit
  ///
  ///      Facility - is the facility code
  ///
  ///      Code - is the facility's status code
  /// </remarks>
  public class HResult
  {
    #region Enums

    /// <summary>
    /// HResult severity enum
    /// </summary>
    public enum Severity
    {
      /// <summary>
      /// Unknown severity
      /// </summary>
      Unknown = -1,
      /// <summary>
      /// Success severity
      /// </summary>
      Success = 0,
      /// <summary>
      /// Info severity
      /// </summary>
      Info = 1,
      /// <summary>
      /// Warning severity
      /// </summary>
      Warning = 2,
      /// <summary>
      /// Errror severity
      /// </summary>
      Error = 3
    }

    /// <summary>
    /// Facility code enum
    /// </summary>
    public enum Facility
    {
      /// <summary>
      /// Unknown
      /// </summary>
      Unknown = -1,
      /// <summary>
      /// Null
      /// </summary>
      Null = 0,
      /// <summary>
      /// RPC
      /// </summary>
      RPC = 1,
      /// <summary>
      /// Dispatch
      /// </summary>
      Dispatch = 2,
      /// <summary>
      /// Storage
      /// </summary>
      Storage = 3,
      /// <summary>
      /// ITF
      /// </summary>
      ITF = 4,
      /// <summary>
      /// Win32
      /// </summary>
      Win32 = 7,
      /// <summary>
      /// Windows
      /// </summary>
      Windows = 8,
      /// <summary>
      /// Security
      /// </summary>
      Security = 9,
      /// <summary>
      /// Control
      /// </summary>
      Control = 10,
      /// <summary>
      /// Cert
      /// </summary>
      Cert = 11,
      /// <summary>
      /// Internet
      /// </summary>
      Internet = 12,
      /// <summary>
      /// MediaServer
      /// </summary>
      MediaServer = 13,
      /// <summary>
      /// MSMQ
      /// </summary>
      MSMQ = 14,
      /// <summary>
      /// SetupAPI
      /// </summary>
      SetupAPI = 15,
      /// <summary>
      /// SCard
      /// </summary>
      SCard = 16,
      /// <summary>
      /// ComPlus
      /// </summary>
      ComPlus = 17,
      /// <summary>
      /// AAF
      /// </summary>
      AAF = 18,
      /// <summary>
      /// ACS
      /// </summary>
      ACS = 20,
      /// <summary>
      /// DPlay
      /// </summary>
      DPlay = 21,
      /// <summary>
      /// UMI
      /// </summary>
      UMI = 22,
      /// <summary>
      /// SXS
      /// </summary>
      SXS = 23,
      /// <summary>
      /// Windows CE
      /// </summary>
      WindowsCE = 24,
      /// <summary>
      /// HTTP
      /// </summary>
      HTTP = 25,
      /// <summary>
      /// BackgroundCopy
      /// </summary>
      BackgroundCopy = 32,
      /// <summary>
      /// Configuration
      /// </summary>
      Configuration = 33,
      /// <summary>
      /// StateManagement
      /// </summary>
      StateManagement = 34,
      /// <summary>
      /// MetaDirectory
      /// </summary>
      MetaDirectory = 35,
      /// <summary>
      /// D3DX
      /// </summary>
      D3DX = 0x877
    }

    #endregion

    #region Variables

    private uint _hresult;
    private int _facilityCode;
    private Facility _facility = Facility.Unknown;
    private int _severityCode;
    private Severity _severity = Severity.Unknown;
    private int _code;

    #endregion

    #region Constructors/Destructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HResult"/> class.
    /// </summary>
    /// <param name="hresult">The hresult code.</param>
    public HResult(int hresult)
    {
      Set(hresult);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the facility type.
    /// </summary>
    /// <value>The facility type.</value>
    public Facility FacilityType
    {
      get { return _facility; }
    }

    /// <summary>
    /// Gets the facility code.
    /// </summary>
    /// <value>The facility code.</value>
    public int FacilityCode
    {
      get { return _facilityCode; }
    }

    /// <summary>
    /// Gets the severity level.
    /// </summary>
    /// <value>The severity level.</value>
    public Severity severityLevel
    {
      get { return _severity; }
    }

    /// <summary>
    /// Gets the code.
    /// </summary>
    /// <value>The code.</value>
    public int Code
    {
      get { return _code; }
    }

    /// <summary>
    /// Gets the DX error string.
    /// </summary>
    /// <value>The DX error string.</value>
    private string DXErrorString
    {
      get { return GetDXErrorString((int)_hresult); }
    }

    /// <summary>
    /// Gets the DX error description.
    /// </summary>
    /// <value>The DX error description.</value>
    private string DXErrorDescription
    {
      get { return DsError.GetErrorText((int)_hresult); }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the specified hresult.
    /// </summary>
    /// <param name="hresult">The hresult.</param>
    public void Set(int hresult)
    {
      _hresult = (uint)hresult;

      _severityCode = (int)(_hresult >> 30);
      _severity = (Severity)_severityCode;

      _facilityCode = (int)(_hresult >> 16);
      _facilityCode = _facilityCode & 0x0FFF;

      if (Enum.IsDefined(typeof (Facility), _facilityCode))
        _facility = (Facility)_facilityCode;
      else
        _facility = Facility.Unknown;

      _code = (int)_hresult & 0x0000FFFF;
    }

    /// <summary>
    /// Characters that will be replaced from the end of DX error message.
    /// </summary>
    static readonly char[] TRIM_CHARS = { '\r', '\n', '.' };

    /// <summary>
    /// Static method which gets the DX error string.
    /// </summary>
    /// <param name="hresult">The hresult.</param>
    /// <returns>the DX error string</returns>
    public static string GetDXErrorString(int hresult)
    {
      return DsError.GetErrorText(hresult).TrimEnd(TRIM_CHARS);
    }

    /// <summary>
    /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override string ToString()
    {
      return _facility == Facility.Unknown
               ? String.Format("0x{0} - {1}:Unknown(0x{2}):0x{3}", _hresult.ToString("X").PadLeft(8, '0'), _severity,
                               _facilityCode.ToString("X").PadLeft(3, '0'), _code.ToString("X").PadLeft(4, '0'))
               : String.Format("0x{0} - {1}:{2}:0x{3}", _hresult.ToString("X").PadLeft(8, '0'), _severity, _facility,
                               _code.ToString("X").PadLeft(4, '0'));
    }

    /// <summary>
    /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </returns>
    public string ToDXString()
    {
      return _hresult == 0
               ? String.Format("No DX Error")
               : String.Format("DX Error: {0} - Error: {1}, Description:{2}", ToString(), DXErrorString,
                               DXErrorDescription);
    }

    /// <summary>
    /// Operator ==s the specified a.
    /// </summary>
    /// <param name="a">A hresult</param>
    /// <param name="b">an int</param>
    /// <returns>bool</returns>
    public static bool operator ==(HResult a, int b)
    {
      return a._hresult == (uint)b;
    }

    /// <summary>
    /// Overloaded lt operator
    /// </summary>
    /// <param name="a">A</param>
    /// <param name="b">B</param>
    /// <returns>true if a._hresult lt b</returns>
    public static bool operator <(HResult a, int b)
    {
      return a._hresult < b;
    }

    /// <summary>
    /// Overloaded gt operator
    /// </summary>
    /// <param name="a">A</param>
    /// <param name="b">B</param>
    /// <returns>true if a._hresult gt b</returns>
    public static bool operator >(HResult a, int b)
    {
      return a._hresult > b;
    }

    /// <summary>
    /// Overloaded lte operator
    /// </summary>
    /// <param name="a">A</param>
    /// <param name="b">B</param>
    /// <returns>true if a._hresult lte b</returns>
    public static bool operator <=(HResult a, int b)
    {
      return a._hresult <= b;
    }

    /// <summary>
    /// Overloaded gte operator
    /// </summary>
    /// <param name="a">A</param>
    /// <param name="b">B</param>
    /// <returns>true if a._hresult gte b</returns>
    public static bool operator >=(HResult a, int b)
    {
      return a._hresult >= b;
    }


    /// <summary>
    /// Operator !=s the specified a.
    /// </summary>
    /// <param name="a">A hresult</param>
    /// <param name="b">an int</param>
    /// <returns>bool</returns>
    public static bool operator !=(HResult a, int b)
    {
      return !(a == b);
    }

    /// <summary>
    /// Determines whether the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <param name="obj">The <see cref="T:System.Object"></see> to compare with the current <see cref="T:System.Object"></see>.</param>
    /// <returns>
    /// true if the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>; otherwise, false.
    /// </returns>
    public override bool Equals(Object obj)
    {
      // Check for null values and compare run-time types.
      if (obj == null || GetType() != obj.GetType())
        return false;
      return _hresult == ((HResult)obj)._hresult;
    }

    /// <summary>
    /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override int GetHashCode()
    {
      return _hresult.GetHashCode();
    }

    /// <summary>
    /// Throw an exception for an HRESULT code.
    /// </summary>
    /// <remarks>
    /// See DirectShowLib.DsError.ThrowExceptionForHR(). The difference here is
    /// that we throw an exception for any non-zero HRESULT, and we use
    /// DXGetErrorString().
    /// </remarks>
    /// <param name="hr">The HRESULT code.</param>
    /// <param name="description">A description that gives error context.</param>
    public static void ThrowException(int hr, string description)
    {
      if (hr == (int)Severity.Success)
      {
        return;
      }

      string errorString = GetDXErrorString(hr);
      string errorDescription = DsError.GetErrorText(hr);

      // If a string is returned, build a COM error from it.
      if (errorString != null)
      {
        errorString = string.Format("0x{0:x} ({1})", hr, errorString);
        if (errorDescription != null)
        {
          errorString += " - " + errorDescription;
        }
        if (description != null)
        {
          errorString += ". " + description;
        }
        throw new TvException(errorString);
      }
      else if (description != null)
      {
        throw new TvException("0x{0:x} - {1}", hr, errorString);
      }
      else
      {
        Marshal.ThrowExceptionForHR(hr);
      }
    }

    #endregion
  }
}