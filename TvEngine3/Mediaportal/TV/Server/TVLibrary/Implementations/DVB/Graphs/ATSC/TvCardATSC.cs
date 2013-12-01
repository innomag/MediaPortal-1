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

using DirectShowLib;
using DirectShowLib.BDA;
using Mediaportal.TV.Server.TVLibrary.Implementations.Helper;
using Mediaportal.TV.Server.TVLibrary.Interfaces;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.Channels;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Interfaces;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Logging;

namespace Mediaportal.TV.Server.TVLibrary.Implementations.DVB.Graphs.ATSC
{
  /// <summary>
  /// Implementation of <see cref="T:Mediaportal.TV.Server.TVLibrary.Interfaces.Interfaces.ITVCard"/> which handles ATSC/QAM tuners with BDA drivers.
  /// </summary>
  public class TvCardATSC : TvCardDvbBase
  {
    #region variables

    /// <summary>
    /// A pre-configured tuning space, used to speed up the tuning process. 
    /// </summary>
    private IATSCTuningSpace _tuningSpace = null;

    #endregion

    #region ctor

    /// <summary>
    /// Initializes a new instance of the <see cref="TvCardATSC"/> class.
    /// </summary>
    /// <param name="epgEvents">The EPG events interface.</param>
    /// <param name="device">The device.</param>
    public TvCardATSC(IEpgEvents epgEvents, DsDevice device)
      : base(epgEvents, device)
    {
      _tunerType = CardType.Atsc;
    }

    #endregion

    #region graphbuilding

    /// <summary>
    /// Create the BDA tuning space for the tuner. This will be used for BDA tuning.
    /// </summary>
    protected override void CreateTuningSpace()
    {
      this.LogDebug("TvCardAtsc: create tuning space");

      // Check if the system already has an appropriate tuning space.
      SystemTuningSpaces systemTuningSpaces = new SystemTuningSpaces();
      ITuningSpaceContainer container = systemTuningSpaces as ITuningSpaceContainer;
      if (container == null)
      {
        this.LogError("TvCardAtsc: failed to get the tuning space container");
        return;
      }

      ITuner tuner = (ITuner) _filterNetworkProvider;
      IEnumTuningSpaces enumTuning;
      container.get_EnumTuningSpaces(out enumTuning);
      try
      {
        ITuningSpace[] spaces = new ITuningSpace[2];
        while (true)
        {
          int fetched;
          enumTuning.Next(1, spaces, out fetched);
          if (fetched != 1)
          {
            break;
          }
          string name;
          spaces[0].get_UniqueName(out name);
          if (name.Equals("MediaPortal ATSC TuningSpace"))
          {
            this.LogDebug("TvCardAtsc: found correct tuningspace");
            _tuningSpace = (IATSCTuningSpace) spaces[0];
            tuner.put_TuningSpace(_tuningSpace);
            Release.ComObject("TuningSpaceContainer", container);
            return;
          }
          Release.ComObject("ITuningSpace", spaces[0]);
        }
      }
      finally
      {
        Release.ComObject("IEnumTuningSpaces", enumTuning);
      }

      // We didn't find our tuning space registered in the system, so create a new one.
      this.LogDebug("TvCardAtsc: create new tuningspace");
      _tuningSpace = (IATSCTuningSpace) new ATSCTuningSpace();
      _tuningSpace.put_UniqueName("MediaPortal ATSC TuningSpace");
      _tuningSpace.put_FriendlyName("MediaPortal ATSC TuningSpace");
      _tuningSpace.put__NetworkType(typeof(ATSCNetworkProvider).GUID);
      _tuningSpace.put_CountryCode(0);
      _tuningSpace.put_InputType(TunerInputType.Antenna);
      _tuningSpace.put_MaxMinorChannel(999);     // the number of minor channels per major channel
      _tuningSpace.put_MaxPhysicalChannel(158);  // 69 for ATSC, 158 for cable (QAM)
      _tuningSpace.put_MaxChannel(99);           // the number of scannable major channels
      _tuningSpace.put_MinMinorChannel(0);
      _tuningSpace.put_MinPhysicalChannel(1);    // 1 for ATSC, 2 for cable (QAM)
      _tuningSpace.put_MinChannel(1);

      IATSCLocator locator = (IATSCLocator) new ATSCLocator();
      locator.put_CarrierFrequency(-1);
      locator.put_PhysicalChannel(-1);
      locator.put_SymbolRate(-1);
      locator.put_Modulation(ModulationType.Mod8Vsb); // 8 VSB is ATSC, 256 QAM is cable
      locator.put_InnerFEC(FECMethod.MethodNotSet);
      locator.put_InnerFECRate(BinaryConvolutionCodeRate.RateNotSet);
      locator.put_OuterFEC(FECMethod.MethodNotSet);
      locator.put_OuterFECRate(BinaryConvolutionCodeRate.RateNotSet);
      locator.put_TSID(-1);

      _tuningSpace.put_DefaultLocator(locator);

      object newIndex;
      container.Add(_tuningSpace, out newIndex);
      Release.ComObject("TuningSpaceContainer", container);

      tuner.put_TuningSpace(_tuningSpace);
    }

    protected override DVBBaseChannel CreateChannel()
    {
      return new ATSCChannel();
    }

    #endregion

    #region tuning & scanning

    /// <summary>
    /// Assemble a BDA tune request for a given channel.
    /// </summary>
    /// <param name="channel">The channel that will be tuned.</param>
    /// <returns>the assembled tune request</returns>
    protected override ITuneRequest AssembleTuneRequest(IChannel channel)
    {
      ATSCChannel atscChannel = channel as ATSCChannel;
      if (atscChannel == null)
      {
        this.LogDebug("TvCardAtsc: channel is not an ATSC/QAM channel!!! {0}", channel.GetType().ToString());
        return null;
      }

      ILocator locator;
      ITuneRequest request;

      int hr = _tuningSpace.get_DefaultLocator(out locator);
      IATSCLocator atscLocator = (IATSCLocator) locator;

      hr |= _tuningSpace.CreateTuneRequest(out request); // Attention: tune request must not be stored, it needs to be created for each new tuning!
      IATSCChannelTuneRequest tuneRequest = (IATSCChannelTuneRequest) request;

      hr |= atscLocator.put_CarrierFrequency((int) atscChannel.Frequency);
      hr |= atscLocator.put_PhysicalChannel(atscChannel.PhysicalChannel);
      hr |= atscLocator.put_Modulation(atscChannel.ModulationType);

      hr |= tuneRequest.put_Channel(atscChannel.MajorChannel);
      hr |= tuneRequest.put_MinorChannel(atscChannel.MinorChannel);
      hr |= tuneRequest.put_Locator(locator);

      if (hr != 0)
      {
        Log.Error("TvCardAtsc: warning, potential error in assemble tune request, hr = 0x{0:x} ({1})", hr, HResult.GetDXErrorString(hr));
      }

      return tuneRequest;
    }

    /// <summary>
    /// Get the device's channel scanning interface.
    /// </summary>
    public override ITVScanning ScanningInterface
    {
      get { return new ATSCScanning(this); }
    }

    /// <summary>
    /// Check if the tuner can tune to a specific channel.
    /// </summary>
    /// <param name="channel">The channel to check.</param>
    /// <returns><c>true</c> if the tuner can tune to the channel, otherwise <c>false</c></returns>
    public override bool CanTune(IChannel channel)
    {
      return channel is ATSCChannel;
    }

    #endregion
  }
}