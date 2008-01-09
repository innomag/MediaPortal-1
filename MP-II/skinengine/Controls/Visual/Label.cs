#region Copyright (C) 2007 Team MediaPortal

/*
    Copyright (C) 2007 Team MediaPortal
    http://www.team-mediaportal.com
 
    This file is part of MediaPortal II

    MediaPortal II is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal II is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal II.  If not, see <http://www.gnu.org/licenses/>.
*/

#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using MediaPortal.Core.Properties;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using Font = SkinEngine.Fonts.Font;
using FontBufferAsset = SkinEngine.Fonts.FontBufferAsset;
using FontManager = SkinEngine.Fonts.FontManager;

namespace SkinEngine.Controls.Visuals
{
  public class Label : Control
  {
    Property _textProperty;
    Property _colorProperty;
    Property _scrollProperty;
    Property _fontProperty;
    FontBufferAsset _asset;

    public Label()
    {
      Init();
      HorizontalAlignment = HorizontalAlignmentEnum.Left;
    }

    public Label(Label lbl)
      : base(lbl)
    {
      Init();
      Text = lbl.Text;
      Color = lbl.Color;
      Scroll = lbl.Scroll;
      Font = lbl.Font;
    }
    void Init()
    {
      _textProperty = new Property("");
      _colorProperty = new Property(Color.White);
      _scrollProperty = new Property(false);
      _fontProperty = new Property("");
      _fontProperty.Attach(new PropertyChangedHandler(OnFontChanged));
      _textProperty.Attach(new PropertyChangedHandler(OnTextChanged));
    }

    public override object Clone()
    {
      return new Label(this);
    }

    void OnTextChanged(Property prop)
    {
      Invalidate();
    }
    void OnFontChanged(Property prop)
    {
      _asset = null;
      Font font = FontManager.GetScript(Font);
      if (font != null)
      {
        _asset = ContentManager.GetFont(font);
      }
    }

    public Property FontProperty
    {
      get
      {
        return _fontProperty;
      }
      set
      {
        _fontProperty = value;
      }
    }

    public string Font
    {
      get
      {
        return _fontProperty.GetValue() as string;
      }
      set
      {
        _fontProperty.SetValue(value);
      }
    }

    /// <summary>
    /// Gets or sets the text property.
    /// </summary>
    /// <value>The text property.</value>
    public Property TextProperty
    {
      get
      {
        return _textProperty;
      }
      set
      {
        _textProperty = value;
      }
    }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    /// <value>The text.</value>
    public string Text
    {
      get
      {
        return _textProperty.GetValue() as string;
      }
      set
      {
        _textProperty.SetValue(value);
      }
    }

    /// <summary>
    /// Gets or sets the color property.
    /// </summary>
    /// <value>The color property.</value>
    public Property ColorProperty
    {
      get
      {
        return _colorProperty;
      }
      set
      {
        _colorProperty = value;
      }
    }

    /// <summary>
    /// Gets or sets the color.
    /// </summary>
    /// <value>The color.</value>
    public Color Color
    {
      get
      {
        return (Color)_colorProperty.GetValue();
      }
      set
      {
        _colorProperty.SetValue(value);
      }
    }

    /// <summary>
    /// Gets or sets the scroll property.
    /// </summary>
    /// <value>The scroll.</value>
    public Property ScrollProperty
    {
      get { return _scrollProperty; }
      set { _scrollProperty = value; }
    }

    public bool Scroll
    {
      get { return (bool)_scrollProperty.GetValue(); }
      set { _scrollProperty.SetValue(value); }
    }

    /// <summary>
    /// measures the size in layout required for child elements and determines a size for the FrameworkElement-derived class.
    /// </summary>
    /// <param name="availableSize">The available size that this element can give to child elements.</param>
    public override void Measure(System.Drawing.Size availableSize)
    {
      _desiredSize = new System.Drawing.Size((int)Width, (int)Height);
      System.Drawing.Size size = new System.Drawing.Size(32, 32);
      if (_asset != null)
      {
        float h = _asset.Font.LineHeight * 1.2f;
        h -= (_asset.Font.LineHeight - _asset.Font.Base);
        size = new Size((int)availableSize.Width, (int)(h));
      }
      if (Width <= 0)
        _desiredSize.Width = ((int)size.Width) - (int)(Margin.X + Margin.W);
      if (Height <= 0)
        _desiredSize.Height = ((int)size.Height) - (int)(Margin.Y + Margin.Z);

      _desiredSize.Width += (int)(Margin.X + Margin.W);
      _desiredSize.Height += (int)(Margin.Y + Margin.Z);
      _transformedSize = _desiredSize;


      _availableSize = new Size(availableSize.Width, availableSize.Height);
    }

    /// <summary>
    /// Arranges the UI element
    /// and positions it in the finalrect
    /// </summary>
    /// <param name="finalRect">The final size that the parent computes for the child element</param>
    public override void Arrange(System.Drawing.Rectangle finalRect)
    {
      _finalRect = new System.Drawing.Rectangle(finalRect.Location, finalRect.Size);
      System.Drawing.Rectangle layoutRect = new System.Drawing.Rectangle(finalRect.X, finalRect.Y, finalRect.Width, finalRect.Height);

      layoutRect.X += (int)(Margin.X);
      layoutRect.Y += (int)(Margin.Y);
      layoutRect.Width -= (int)(Margin.X + Margin.W);
      layoutRect.Height -= (int)(Margin.Y + Margin.Z);
      ActualPosition = new Vector3(layoutRect.Location.X, layoutRect.Location.Y, 1.0f); ;
      ActualWidth = layoutRect.Width;
      ActualHeight = layoutRect.Height;

      if (!IsArrangeValid)
      {
        IsArrangeValid = true;
        InitializeBindings();
        InitializeTriggers();
      }
    }

    /// <summary>
    /// Renders the visual
    /// </summary>
    public override void DoRender()
    {
      if (_asset == null) return;
      ColorValue color = ColorValue.FromColor(this.Color);

      base.DoRender();
      GraphicsDevice.Device.Transform.World = SkinContext.FinalMatrix.Matrix;
      float totalWidth;
      float size = _asset.Font.Size;
      System.Drawing.Rectangle rect = new System.Drawing.Rectangle((int)ActualPosition.X, (int)ActualPosition.Y, (int)ActualWidth, (int)ActualHeight);
      SkinEngine.Fonts.Font.Align align = SkinEngine.Fonts.Font.Align.Left;
      if (HorizontalAlignment == HorizontalAlignmentEnum.Right)
        align = SkinEngine.Fonts.Font.Align.Right;
      else if (HorizontalAlignment == HorizontalAlignmentEnum.Center)
        align = SkinEngine.Fonts.Font.Align.Center;


      if (rect.Height < _asset.Font.LineHeight * 1.2f)
      {
        rect.Height = (int)(_asset.Font.LineHeight * 1.2f);
      }
      rect.Y -= (int)(_asset.Font.LineHeight - _asset.Font.Base);
      _asset.Draw(Text, rect, align, size, color, Scroll, out totalWidth);
    }
  }
}

