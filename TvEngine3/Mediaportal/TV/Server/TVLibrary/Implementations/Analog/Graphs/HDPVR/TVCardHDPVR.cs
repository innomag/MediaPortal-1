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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DirectShowLib;
using Mediaportal.TV.Server.TVDatabase.Entities.Enums;
using Mediaportal.TV.Server.TVLibrary.Implementations.Analog.QualityControl;
using Mediaportal.TV.Server.TVLibrary.Implementations.Helper;
using Mediaportal.TV.Server.TVLibrary.Interfaces;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.Analog;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.Channels;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Interfaces;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Logging;

namespace Mediaportal.TV.Server.TVLibrary.Implementations.Analog.Graphs.HDPVR
{
  /// <summary>
  /// Class for handling supported capture cards, including the Hauppauge HD PVR and Colossus.
  /// </summary>
  public class TvCardHDPVR : TvCardBase
  {


    #region constants

    // Assume the capture card is a Hauppauge HD PVR by default.
    private readonly string _deviceType = "HDPVR";
    private readonly string _crossbarDeviceName = "Hauppauge HD PVR Crossbar";
    private readonly string _captureDeviceName = "Hauppauge HD PVR Capture Device";
    private readonly string _encoderDeviceName = "Hauppauge HD PVR Encoder";

    #endregion

    #region imports

    [ComImport, Guid("fc50bed6-fe38-42d3-b831-771690091a6e")]
    private class TsWriter { }

    #endregion

    #region variables

    private DsROTEntry _rotEntry;
    private ICaptureGraphBuilder2 _capBuilder;
    private DsDevice _crossBarDevice;
    private DsDevice _captureDevice;
    private DsDevice _encoderDevice;
    private IBaseFilter _filterCrossBar;
    private IBaseFilter _filterCapture;
    private IBaseFilter _filterEncoder;
    private IBaseFilter _filterTsWriter;
    private Configuration _configuration;
    private IQuality _qualityControl;

    /// <summary>
    /// The mapping of the video input sources to their pin index
    /// </summary>
    private Dictionary<AnalogChannel.VideoInputType, int> _videoPinMap;

    /// <summary>
    /// The mapping of the video input sources to their related audio pin index
    /// </summary>
    private Dictionary<AnalogChannel.VideoInputType, int> _videoPinRelatedAudioMap;

    /// <summary>
    /// The mapping of the audio input sources to their pin index
    /// </summary>
    private Dictionary<AnalogChannel.AudioInputType, int> _audioPinMap;

    private int _videoOutPinIndex;
    private int _audioOutPinIndex;

    #endregion

    #region ctor

    ///<summary>
    /// Constructor for a capture card device.
    ///</summary>
    ///<param name="device">A crossbar device for a supported capture card.</param>
    public TvCardHDPVR(DsDevice device)
      : base(device)
    {
      // Determine what type of card this is.
      if (device.Name.Contains("Colossus"))
      {
        Match match = Regex.Match(device.Name, @".*?(\d+)$");
        int deviceNumber = 0;
        if (match.Success)
        {
          deviceNumber = Convert.ToInt32(match.Groups[1].Value);
        }
        _deviceType = "Colossus";
        _crossbarDeviceName = device.Name;
        _captureDeviceName = "Hauppauge Colossus Capture " + deviceNumber;
        _encoderDeviceName = "Hauppauge Colossus TS Encoder " + deviceNumber;
      }

      _supportsSubChannels = true;
      _tunerType = CardType.Analog;
      _configuration = Configuration.readConfiguration(_cardId, _name, _devicePath);
      Configuration.writeConfiguration(_configuration);
    }

    #endregion

    #region public methods

    /// <summary>
    /// Check if the tuner can tune to a specific channel.
    /// </summary>
    /// <param name="channel">The channel to check.</param>
    /// <returns><c>true</c> if the tuner can tune to the channel, otherwise <c>false</c></returns>
    public override bool CanTune(IChannel channel)
    {
      // My understanding is that the HD-PVR and Colossus are not able to capture audio-only streams. The
      // driver doesn't seem to create PMT if a video stream is not detected.
      if (channel is AnalogChannel && channel.MediaType != MediaTypeEnum.Radio)
      {
        return true;
      }
      return true;
    }

    #endregion

    #region subchannel management

    /// <summary>
    /// Allocate a new subchannel instance.
    /// </summary>
    /// <param name="channel">The service or channel to associate with the subchannel.</param>
    /// <returns>a handle for the subchannel</returns>
    protected override int CreateNewSubChannel(IChannel channel)
    {
      int id = _subChannelId++;
      this.LogInfo("TvCardHdPvr: new subchannel, ID = {0}, subchannel count = {1}", id, _mapSubChannels.Count);
      HDPVRChannel subChannel = new HDPVRChannel(id, this, _filterTsWriter);
      subChannel.Parameters = Parameters;
      subChannel.CurrentChannel = channel;
      _mapSubChannels[id] = subChannel;
      FireNewSubChannelEvent(id);
      return id;
    }

    #endregion

    #region quality control

    /// <summary>
    /// Get/Set the quality
    /// </summary>
    public override IQuality Quality
    {
      get { return _qualityControl; }
    }

    /// <summary>
    /// Property which returns true if card supports quality control
    /// </summary>
    public override bool SupportsQualityControl
    {
      get
      {
        if (!_isDeviceInitialised)
        {
          BuildGraph();
        }
        return _qualityControl != null;
      }
    }

    /// <summary>
    /// Reloads the quality control configuration
    /// </summary>
    public override void ReloadCardConfiguration()
    {
      if (_qualityControl != null)
      {
        _configuration = Configuration.readConfiguration(_cardId, _name, _devicePath);
        Configuration.writeConfiguration(_configuration);
        _qualityControl.SetConfiguration(_configuration);
      }
    }

    #endregion

    #region properties

    /// <summary>
    /// Update the tuner signal status statistics.
    /// </summary>
    /// <param name="force"><c>True</c> to force the status to be updated (status information may be cached).</param>
    protected override void UpdateSignalStatus(bool force)
    {
      if (!_isDeviceInitialised)
      {
        _tunerLocked = false;
        _signalLevel = 0;
        _signalQuality = 0;
      }
      else
      {
        _tunerLocked = true;
        _signalLevel = 100;
        _signalQuality = 100;
      }
    }

    #endregion

    #region Disposable

    /// <summary>
    /// Disposes this instance.
    /// </summary>
    public override void Dispose()
    {
      if (_graphBuilder == null)
        return;
      this.LogDebug("HDPVR:  Dispose()");

      FreeAllSubChannels();
      // Decompose the graph
      IMediaControl mediaCtl = (_graphBuilder as IMediaControl);
      if (mediaCtl == null)
      {
        throw new TvException("Can not convert graphBuilder to IMediaControl");
      }
      // Decompose the graph
      mediaCtl.Stop();

      base.Dispose();

      FilterGraphTools.RemoveAllFilters(_graphBuilder);
      this.LogDebug("HDPVR:  All filters removed");
      if (_filterCrossBar != null)
      {
        while (Release.ComObject(_filterCrossBar) > 0) { }
        _filterCrossBar = null;
      }
      if (_filterCapture != null)
      {
        while (Release.ComObject(_filterCapture) > 0) { }
        _filterCapture = null;
      }
      if (_filterEncoder != null)
      {
        while (Release.ComObject(_filterEncoder) > 0) { }
        _filterEncoder = null;
      }
      if (_filterTsWriter != null)
      {
        while (Release.ComObject(_filterTsWriter) > 0) { }
        _filterTsWriter = null;
      }
      _rotEntry.Dispose();
      Release.ComObject("Graphbuilder", _graphBuilder);
      _graphBuilder = null;
      DevicesInUse.Instance.Remove(_tunerDevice);
      if (_crossBarDevice != null)
      {
        DevicesInUse.Instance.Remove(_crossBarDevice);
        _crossBarDevice = null;
      }
      if (_captureDevice != null)
      {
        DevicesInUse.Instance.Remove(_captureDevice);
        _captureDevice = null;
      }
      if (_encoderDevice != null)
      {
        DevicesInUse.Instance.Remove(_encoderDevice);
        _encoderDevice = null;
      }
      _isDeviceInitialised = false;
      this.LogDebug("HDPVR:  dispose completed");
    }

    #endregion

    #region graph handling

    /// <summary>
    /// Builds the directshow graph for this analog tvcard
    /// </summary>
    public override void BuildGraph()
    {
      if (_cardId == 0)
      {
        _configuration = Configuration.readConfiguration(_cardId, _name, _devicePath);
        Configuration.writeConfiguration(_configuration);
      }

      _lastSignalUpdate = DateTime.MinValue;
      _tunerLocked = false;
      this.LogDebug("HDPVR: build graph");
      try
      {
        if (_isDeviceInitialised)
        {
          this.LogDebug("HDPVR: graph already built!");
          throw new TvException("Graph already built");
        }
        _graphBuilder = (IFilterGraph2)new FilterGraph();
        _rotEntry = new DsROTEntry(_graphBuilder);
        _capBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
        _capBuilder.SetFiltergraph(_graphBuilder);
        AddCrossBarFilter();
        AddCaptureFilter();
        AddEncoderFilter();
        AddTsWriterFilterToGraph();
        _qualityControl = QualityControlFactory.createQualityControl(_configuration, _filterEncoder, _filterCapture,
                                                                     null, null);
        if (_qualityControl == null)
        {
          this.LogDebug("HDPVR: No quality control support found");
        }

        _isDeviceInitialised = true;
        _configuration.Graph.Crossbar.Name = _crossBarDevice.Name;
        _configuration.Graph.Crossbar.VideoPinMap = _videoPinMap;
        _configuration.Graph.Crossbar.AudioPinMap = _audioPinMap;
        _configuration.Graph.Crossbar.VideoPinRelatedAudioMap = _videoPinRelatedAudioMap;
        _configuration.Graph.Crossbar.VideoOut = _videoOutPinIndex;
        _configuration.Graph.Crossbar.AudioOut = _audioOutPinIndex;
        _configuration.Graph.Capture.Name = _captureDevice.Name;
        _configuration.Graph.Capture.FrameRate = -1d;
        _configuration.Graph.Capture.ImageHeight = -1;
        _configuration.Graph.Capture.ImageWidth = -1;
        Configuration.writeConfiguration(_configuration);
      }
      catch (Exception ex)
      {
        this.LogError(ex);
        Dispose();
        _isDeviceInitialised = false;
        throw;
      }
    }

    private void AddCrossBarFilter()
    {
      this.LogDebug("HDPVR: Add Crossbar Filter");
      //get list of all crossbar devices installed on this system
      _crossBarDevice = _tunerDevice;
      IBaseFilter tmp;
      int hr;
      try
      {
        //add the crossbar to the graph
        hr = _graphBuilder.AddSourceFilterForMoniker(_crossBarDevice.Mon, null, _crossBarDevice.Name, out tmp);
      }
      catch (Exception)
      {
        this.LogDebug("HDPVR: cannot add filter to graph");
        throw new TvException("Unable to add crossbar to graph");
      }
      if (hr == 0)
      {
        _filterCrossBar = tmp;
        CheckCapabilities();
        return;
      }
      this.LogDebug("HDPVR: cannot add filter to graph");
      throw new TvException("Unable to add crossbar to graph");
    }

    private void AddCaptureFilter()
    {
      DsDevice[] devices;
      this.LogDebug("HDPVR: Add Capture Filter");
      //get a list of all video capture devices
      try
      {
        devices = DsDevice.GetDevicesOfCat(FilterCategory.AMKSCapture);
        devices = DeviceSorter.Sort(devices, _tunerDevice, _filterCrossBar, _captureDevice, _filterEncoder);
      }
      catch (Exception)
      {
        this.LogDebug("HDPVR: AddTvCaptureFilter no tvcapture devices found");
        return;
      }
      if (devices.Length == 0)
      {
        this.LogDebug("HDPVR: AddTvCaptureFilter no tvcapture devices found");
        return;
      }
      //try each video capture filter
      for (int i = 0; i < devices.Length; i++)
      {
        if (devices[i].Name != _captureDeviceName)
        {
          continue;
        }
        this.LogDebug("HDPVR: AddTvCaptureFilter try:{0} {1}", devices[i].Name, i);
        // if video capture filter is in use, then we can skip it
        if (DevicesInUse.Instance.IsUsed(devices[i]))
        {
          continue;
        }
        IBaseFilter tmp;
        int hr;
        try
        {
          // add video capture filter to graph
          hr = _graphBuilder.AddSourceFilterForMoniker(devices[i].Mon, null, devices[i].Name, out tmp);
        }
        catch (Exception)
        {
          this.LogDebug("HDPVR: cannot add filter to graph");
          continue;
        }
        if (hr != 0)
        {
          //cannot add video capture filter to graph, try next one
          if (tmp != null)
          {
            _graphBuilder.RemoveFilter(tmp);
            Release.ComObject("TvCaptureFilter", tmp);
          }
          continue;
        }
        // connect crossbar->video capture filter
        hr = _capBuilder.RenderStream(null, null, _filterCrossBar, null, tmp);
        if (hr == 0)
        {
          // That worked. Since most crossbar devices require 2 connections from
          // crossbar->video capture filter, we do it again to connect the 2nd pin
          _capBuilder.RenderStream(null, null, _filterCrossBar, null, tmp);
          _filterCapture = tmp;
          _captureDevice = devices[i];
          DevicesInUse.Instance.Add(_captureDevice);
          this.LogDebug("HDPVR: AddTvCaptureFilter connected to crossbar successfully");
          break;
        }
        // cannot connect crossbar->video capture filter, remove filter from graph
        // cand continue with the next vieo capture filter
        this.LogDebug("HDPVR: AddTvCaptureFilter failed to connect to crossbar");
        _graphBuilder.RemoveFilter(tmp);
        Release.ComObject("capture filter", tmp);
      }
      if (_filterCapture == null)
      {
        this.LogError("HDPVR: unable to add TvCaptureFilter to graph");
        //throw new TvException("Unable to add TvCaptureFilter to graph");
      }
    }

    private void AddEncoderFilter()
    {
      DsDevice[] devices;
      this.LogDebug("HDPVR: AddEncoderFilter");
      // first get all encoder filters available on this system
      try
      {
        devices = DsDevice.GetDevicesOfCat(FilterCategory.WDMStreamingEncoderDevices);
        devices = DeviceSorter.Sort(devices, _tunerDevice, _filterCrossBar, _captureDevice, _filterEncoder);
      }
      catch (Exception)
      {
        this.LogDebug("HDPVR: AddTvEncoderFilter no encoder devices found (Exception)");
        return;
      }

      if (devices == null)
      {
        this.LogDebug("HDPVR: AddTvEncoderFilter no encoder devices found (devices == null)");
        return;
      }

      if (devices.Length == 0)
      {
        this.LogDebug("HDPVR: AddTvEncoderFilter no encoder devices found");
        return;
      }

      //for each encoder
      this.LogDebug("HDPVR: AddTvEncoderFilter found:{0} encoders", devices.Length);
      for (int i = 0; i < devices.Length; i++)
      {
        if (devices[i].Name != _encoderDeviceName)
        {
          continue;
        }

        //if encoder is in use, we can skip it
        if (DevicesInUse.Instance.IsUsed(devices[i]))
        {
          this.LogDebug("HDPVR:  skip :{0} (inuse)", devices[i].Name);
          continue;
        }

        this.LogDebug("HDPVR:  try encoder:{0} {1}", devices[i].Name, i);
        IBaseFilter tmp;
        int hr;
        try
        {
          //add encoder filter to graph
          hr = _graphBuilder.AddSourceFilterForMoniker(devices[i].Mon, null, devices[i].Name, out tmp);
        }
        catch (Exception)
        {
          this.LogDebug("HDPVR: cannot add filter {0} to graph", devices[i].Name);
          continue;
        }
        if (hr != 0)
        {
          //failed to add filter to graph, continue with the next one
          if (tmp != null)
          {
            _graphBuilder.RemoveFilter(tmp);
            Release.ComObject("TvEncoderFilter", tmp);
          }
          continue;
        }
        if (tmp == null)
        {
          continue;
        }
        hr = _capBuilder.RenderStream(null, null, _filterCapture, null, tmp);
        if (hr == 0)
        {
          // That worked. Since most crossbar devices require 2 connections from
          // crossbar->video capture filter, we do it again to connect the 2nd pin
          _capBuilder.RenderStream(null, null, _filterCapture, null, tmp);
          _filterEncoder = tmp;
          _encoderDevice = devices[i];
          DevicesInUse.Instance.Add(_encoderDevice);
          this.LogDebug("HDPVR: AddTvEncoderFilter connected to catpure successfully");
          //and we're done
          return;
        }
        // cannot connect crossbar->video capture filter, remove filter from graph
        // cand continue with the next vieo capture filter
        this.LogDebug("HDPVR: AddTvEncoderFilter failed to connect to capture");
        _graphBuilder.RemoveFilter(tmp);
        Release.ComObject("capture filter", tmp);
      }
      this.LogDebug("HDPVR: AddTvEncoderFilter no encoder found");
    }

    private void AddTsWriterFilterToGraph()
    {
      if (_filterTsWriter == null)
      {
        this.LogDebug("HDPVR: Add Mediaportal TsWriter filter");
        _filterTsWriter = FilterLoader.LoadFilterFromDll("TsWriter.ax", typeof(TsWriter).GUID, true);
        int hr = _graphBuilder.AddFilter(_filterTsWriter, "MediaPortal Ts Writer");
        if (hr != 0)
        {
          this.LogError("HDPVR:  Add main Ts Analyzer returns:0x{0:X}", hr);
          throw new TvException("Unable to add Ts Analyzer filter");
        }
        IPin pinOut = DsFindPin.ByDirection(_filterEncoder, PinDirection.Output, 0);
        if (pinOut == null)
        {
          this.LogError("HDPVR:  Unable to find output pin on the encoder filter");
          throw new TvException("unable to find output pin on the encoder filter");
        }
        IPin pinIn = DsFindPin.ByDirection(_filterTsWriter, PinDirection.Input, 0);
        if (pinIn == null)
        {
          this.LogError("HDPVR:  Unable to find the input pin on ts analyzer filter");
          throw new TvException("Unable to find the input pin on ts analyzer filter");
        }
        //Log.this.LogInfo("HDPVR: Render [Encoder]->[TsWriter]");
        hr = _graphBuilder.Connect(pinOut, pinIn);
        Release.ComObject("pinTsWriterIn", pinIn);
        Release.ComObject("pinEncoderOut", pinOut);
        if (hr != 0)
        {
          this.LogError("HDPVR:  Unable to connect encoder to ts analyzer filter :0x{0:X}", hr);
          throw new TvException("unable to connect encoder to ts analyzer filter");
        }
        this.LogDebug("HDPVR: AddTsWriterFilterToGraph connected to encoder successfully");
      }
    }

    #endregion

    #region private helper

    /// <summary>
    /// Actually tune to a channel.
    /// </summary>
    /// <param name="channel">The channel to tune to.</param>
    protected override void PerformTuning(IChannel channel)
    {
      this.LogDebug("HDPVR: Tune");
      AnalogChannel analogChannel = channel as AnalogChannel;
      if (analogChannel == null)
      {
        throw new NullReferenceException();
      }
      AnalogChannel previousChannel = _previousChannel as AnalogChannel;
      if (_previousChannel != null && previousChannel == null)
      {
        throw new NullReferenceException();
      }

      // Set up the crossbar.
      IAMCrossbar crossBarFilter = _filterCrossBar as IAMCrossbar;

      if (_previousChannel == null || previousChannel.VideoSource != analogChannel.VideoSource)
      {
        // Video
        if (_videoPinMap.ContainsKey(analogChannel.VideoSource))
        {
          this.LogDebug("HDPVR:   video input -> {0}", analogChannel.VideoSource);
          crossBarFilter.Route(_videoOutPinIndex, _videoPinMap[analogChannel.VideoSource]);
        }

        // Automatic Audio
        if (analogChannel.AudioSource == AnalogChannel.AudioInputType.Automatic)
        {
          if (_videoPinRelatedAudioMap.ContainsKey(analogChannel.VideoSource))
          {
            this.LogDebug("HDPVR:   audio input -> (auto)");
            crossBarFilter.Route(_audioOutPinIndex, _videoPinRelatedAudioMap[analogChannel.VideoSource]);
          }
        }
      }

      // Audio
      if ((_previousChannel == null || previousChannel.AudioSource != analogChannel.AudioSource) &&
        analogChannel.AudioSource != AnalogChannel.AudioInputType.Automatic &&
        _audioPinMap.ContainsKey(analogChannel.AudioSource))
      {
        this.LogDebug("HDPVR:   audio input -> {0}", analogChannel.AudioSource);
        crossBarFilter.Route(_audioOutPinIndex, _audioPinMap[analogChannel.AudioSource]);
      }

      _previousChannel = analogChannel;
      this.LogDebug("HDPVR: Tuned to channel {0}", channel.Name);
    }

    /// <summary>
    /// Checks the capabilities
    /// </summary>
    private void CheckCapabilities()
    {
      IAMCrossbar crossBarFilter = _filterCrossBar as IAMCrossbar;
      if (crossBarFilter != null)
      {
        int outputs, inputs;
        crossBarFilter.get_PinCounts(out outputs, out inputs);
        _videoOutPinIndex = -1;
        _audioOutPinIndex = -1;
        _videoPinMap = new Dictionary<AnalogChannel.VideoInputType, int>();
        _audioPinMap = new Dictionary<AnalogChannel.AudioInputType, int>();
        _videoPinRelatedAudioMap = new Dictionary<AnalogChannel.VideoInputType, int>();
        int relatedPinIndex;
        PhysicalConnectorType connectorType;
        for (int i = 0; i < outputs; ++i)
        {
          crossBarFilter.get_CrossbarPinInfo(false, i, out relatedPinIndex, out connectorType);
          if (connectorType == PhysicalConnectorType.Video_VideoDecoder)
          {
            _videoOutPinIndex = i;
          }
          if (connectorType == PhysicalConnectorType.Audio_AudioDecoder)
          {
            _audioOutPinIndex = i;
          }
        }

        int audioLine = 0;
        int audioSPDIF = 0;
        int audioAux = 0;
        int videoCvbsNr = 0;
        int videoSvhsNr = 0;
        int videoYrYbYNr = 0;
        int videoRgbNr = 0;
        int videoHdmiNr = 0;
        for (int i = 0; i < inputs; ++i)
        {
          crossBarFilter.get_CrossbarPinInfo(true, i, out relatedPinIndex, out connectorType);
          this.LogDebug(" crossbar pin:{0} type:{1}", i, connectorType);
          switch (connectorType)
          {
            case PhysicalConnectorType.Audio_Tuner:
              _audioPinMap.Add(AnalogChannel.AudioInputType.Tuner, i);
              break;
            case PhysicalConnectorType.Video_Tuner:
              _videoPinMap.Add(AnalogChannel.VideoInputType.Tuner, i);
              _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.Tuner, relatedPinIndex);
              break;
            case PhysicalConnectorType.Audio_Line:
              audioLine++;
              switch (audioLine)
              {
                case 1:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.LineInput1, i);
                  break;
                case 2:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.LineInput2, i);
                  break;
                case 3:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.LineInput3, i);
                  break;
              }
              break;
            case PhysicalConnectorType.Audio_SPDIFDigital:
              audioSPDIF++;
              switch (audioSPDIF)
              {
                case 1:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.SPDIFInput1, i);
                  break;
                case 2:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.SPDIFInput2, i);
                  break;
                case 3:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.SPDIFInput3, i);
                  break;
              }
              break;
            case PhysicalConnectorType.Audio_AUX:
              audioAux++;
              switch (audioAux)
              {
                case 1:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.AUXInput1, i);
                  break;
                case 2:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.AUXInput2, i);
                  break;
                case 3:
                  _audioPinMap.Add(AnalogChannel.AudioInputType.AUXInput3, i);
                  break;
              }
              break;
            case PhysicalConnectorType.Video_Composite:
              videoCvbsNr++;
              switch (videoCvbsNr)
              {
                case 1:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.VideoInput1, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.VideoInput1, relatedPinIndex);
                  break;
                case 2:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.VideoInput2, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.VideoInput2, relatedPinIndex);
                  break;
                case 3:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.VideoInput3, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.VideoInput3, relatedPinIndex);
                  break;
              }
              break;
            case PhysicalConnectorType.Video_SVideo:
              videoSvhsNr++;
              switch (videoSvhsNr)
              {
                case 1:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.SvhsInput1, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.SvhsInput1, relatedPinIndex);
                  break;
                case 2:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.SvhsInput2, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.SvhsInput2, relatedPinIndex);
                  break;
                case 3:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.VideoInput3, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.VideoInput3, relatedPinIndex);
                  break;
              }
              break;
            case PhysicalConnectorType.Video_RGB:
              videoRgbNr++;
              switch (videoRgbNr)
              {
                case 1:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.RgbInput1, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.RgbInput1, relatedPinIndex);
                  break;
                case 2:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.RgbInput2, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.RgbInput2, relatedPinIndex);
                  break;
                case 3:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.SvhsInput3, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.SvhsInput3, relatedPinIndex);
                  break;
              }
              break;
            case PhysicalConnectorType.Video_YRYBY:
              videoYrYbYNr++;
              switch (videoYrYbYNr)
              {
                case 1:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.YRYBYInput1, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.YRYBYInput1, relatedPinIndex);
                  break;
                case 2:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.YRYBYInput2, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.YRYBYInput2, relatedPinIndex);
                  break;
                case 3:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.YRYBYInput3, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.YRYBYInput3, relatedPinIndex);
                  break;
              }
              break;
            case PhysicalConnectorType.Video_SerialDigital:
              videoHdmiNr++;
              switch (videoHdmiNr)
              {
                case 1:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.HdmiInput1, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.HdmiInput1, relatedPinIndex);
                  break;
                case 2:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.HdmiInput2, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.HdmiInput2, relatedPinIndex);
                  break;
                case 3:
                  _videoPinMap.Add(AnalogChannel.VideoInputType.HdmiInput3, i);
                  _videoPinRelatedAudioMap.Add(AnalogChannel.VideoInputType.HdmiInput3, relatedPinIndex);
                  break;
              }
              break;
          }
        }
      }
    }

    #endregion
  }
}