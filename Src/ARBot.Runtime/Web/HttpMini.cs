using System;
using System.IO;
using System.Text;

namespace ARBot.Robot.Web
{
    /// <summary>Rozebrany prvni radek pozadavku.</summary>
    public readonly struct HttpRequestLine
    {
        /// <summary>Podarilo se radek rozebrat?</summary>
        public readonly bool Ok;
        public readonly string Method;
        /// <summary>Cesta BEZ query stringu (ten se pri routovani zahazuje).</summary>
        public readonly string Path;
        /// <summary>Query string bez uvodniho '?' (prazdny, kdyz nebyl).</summary>
        public readonly string Query;

        public HttpRequestLine(bool ok, string method, string path, string query)
        {
            Ok = ok; Method = method; Path = path; Query = query;
        }
    }

    /// <summary>
    /// <b>Nejmensi HTTP, jake staci prohlizeci.</b> Jen prvni radek a hlavicky do prazdneho radku;
    /// odpoved vzdy s <c>Content-Length</c> a <c>Connection: close</c>. Zadny keep-alive, zadny
    /// chunked, zadne cteni tela (<c>POST /stop</c> ho nepotrebuje).
    ///
    /// <para><b>Proc vlastni a ne <see cref="System.Net.HttpListener"/>:</b> ten na Windows bez
    /// administratorskych prav neprijme jiny prefix nez <c>localhost</c> - <c>http://+:port/</c>
    /// i <c>http://*:port/</c> skonci „Pristup byl odepren" (namereno 4. 9. 2026), zatimco na Linuxu
    /// by fungoval. Ladil by se tedy jiny stav, nez jaky bezi na Pi. Tenhle kod se chova na obou
    /// platformach stejne a nepotrebuje URL ACL. Viz doc/headless.md.</para>
    ///
    /// <para>Pracuje nad <see cref="Stream"/>, nikoliv nad socketem, takze jde otestovat nad
    /// <c>MemoryStream</c> bez site.</para>
    /// </summary>
    public static class HttpMini
    {
        /// <summary>Strop na hlavicku pozadavku; delsi se odmitne (413). Ochrana proti zaplave.</summary>
        public const int MaxHeaderBytes = 8 * 1024;

        /// <summary>
        /// Rozebere „GET /cesta?dotaz HTTP/1.1". Pri nesmyslu vraci <c>Ok = false</c> - volajici
        /// z toho udela 400, nikdy vyjimku.
        /// </summary>
        public static HttpRequestLine ParseRequestLine(string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine)) return default;

            var parts = firstLine.Split(' ');
            if (parts.Length < 3) return default;

            string method = parts[0];
            string target = parts[1];
            if (method.Length == 0 || target.Length == 0 || target[0] != '/') return default;

            int q = target.IndexOf('?');
            string path = q >= 0 ? target.Substring(0, q) : target;
            string query = q >= 0 ? target.Substring(q + 1) : string.Empty;
            return new HttpRequestLine(true, method, path, query);
        }

        /// <summary>
        /// Precte hlavicku az po prazdny radek (CRLFCRLF i LFLF - prohlizec posila prvni, curl
        /// a telnet druhe). Vraci <c>null</c>, kdyz spojeni skoncilo driv nebo hlavicka prekrocila
        /// <paramref name="maxBytes"/>.
        ///
        /// <para>Cte po bajtech zamerne: hlavicka je male desitky bajtu, takze na vykonu nezalezi,
        /// a hlavne se tim <b>neprecte telo</b> - to by u <c>POST</c> zustalo v bufferu.</para>
        /// </summary>
        public static string ReadHeader(Stream s, int maxBytes = MaxHeaderBytes)
        {
            if (s == null) return null;

            var sb = new StringBuilder(256);
            int konec = 0;   // kolik znaku ukoncovaci sekvence uz sedi
            for (int n = 0; n < maxBytes; n++)
            {
                int b = s.ReadByte();
                if (b < 0) return null;          // spojeni zavreno pred koncem hlavicky

                char ch = (char)b;
                sb.Append(ch);

                if (ch == '\n')
                {
                    if (konec == 1) return sb.ToString();   // druhy konec radku za sebou = prazdny radek
                    konec = 1;
                }
                else if (ch != '\r')
                {
                    konec = 0;
                }
            }
            return null;   // prekroceno
        }

        /// <summary>Odesle odpoved s telem. Hlavicky jsou zamerne minimalni.</summary>
        public static void WriteResponse(Stream s, int status, string contentType, byte[] body)
        {
            if (s == null) return;
            body ??= Array.Empty<byte>();

            var head = new StringBuilder(160);
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            if (!string.IsNullOrEmpty(contentType))
                head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            // Nahled jsou ziva data - cache by ukazovala minulost.
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n\r\n");

            var bytes = Encoding.ASCII.GetBytes(head.ToString());
            s.Write(bytes, 0, bytes.Length);
            if (body.Length > 0) s.Write(body, 0, body.Length);
            s.Flush();
        }

        /// <summary>Odesle textovou odpoved (UTF-8).</summary>
        public static void WriteText(Stream s, int status, string text)
            => WriteResponse(s, status, "text/plain; charset=utf-8",
                             Encoding.UTF8.GetBytes(text ?? string.Empty));

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "OK",
        };
    }
}
