#region Copyright (C) 2007-2010 Team MediaPortal

/*
    Copyright (C) 2007-2010 Team MediaPortal
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

using System.Net;
using HttpServer;

namespace UPnP.Infrastructure.Utils
{
  public class HttpServerHelper
  {
    /// <summary>
    /// Given an HTTP request, this method returns the client's IP address.
    /// </summary>
    /// <param name="request">Http client request.</param>
    /// <returns><see cref="string"/> instance containing the client's IP address. The returned IP address can be
    /// parsed by calling <see cref="IPAddress.Parse"/>.</returns>
    public static string GetRemoteAddress(IHttpRequest request)
    {
      return request.Headers["remote_addr"];
    }
  }
}