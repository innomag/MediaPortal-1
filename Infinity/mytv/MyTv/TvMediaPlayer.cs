using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TvControl;
namespace MyTv
{
  public class TvMediaPlayer : MediaPlayer
  {
    #region delegates
    private delegate void StopTimeshiftingDelegate(VirtualCard card);
    #endregion
    #region variables
    VirtualCard _card;
    Exception _exception;
    #endregion

    #region ctor
    /// <summary>
    /// Initializes a new instance of the <see cref="TvMediaPlayer"/> class.
    /// </summary>
    /// <param name="card">The card.</param>
    public TvMediaPlayer(VirtualCard card)
    {
      _card = card;
      _exception = null;
      MediaFailed += new EventHandler<ExceptionEventArgs>(TvMediaPlayer_MediaFailed);
    }

    void TvMediaPlayer_MediaFailed(object sender, ExceptionEventArgs e)
    {
      _exception = e.ErrorException;
    }
    #endregion
    public string ErrorMessage
    {
      get
      {
        if (_exception == null) return "";
        return _exception.Message;
      }
    }
    public bool HasError
    {
      get
      {
        return (_exception != null);
      }
    }

    #region IDisposable
    /// <summary>
    /// Disposes this instance.
    /// </summary>
    public void Dispose(bool stopTimeShifting)
    {
      base.Stop();
      base.Close();
      if (_card != null && stopTimeShifting)
      {
        StopTimeshiftingDelegate starter = new StopTimeshiftingDelegate(this.DoStopTimeshifting);
        starter.BeginInvoke(_card,null, null);
      }
      TvPlayerCollection.Instance.Release(this);
    }

    void DoStopTimeshifting(VirtualCard card)
    {
      if (card != null)
      {
        if (card.IsTimeShifting)
        {
          card.StopTimeShifting();
        }
      }
    }
    #endregion
  }
}
