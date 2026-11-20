using System;

namespace Fodinha.Server
{
    /// <summary>
    /// Cabeçalhos para servir a versão web (WebGL) do jogo a partir de wwwroot/.
    /// O Unity gera os arquivos compactados (.gz/.br); o servidor informa o tipo real e a compactação,
    /// para o navegador descompactar sozinho (mais rápido que descompactar em JavaScript).
    /// </summary>
    public static class WebGlFiles
    {
        public sealed class Headers
        {
            public string ContentType;
            public string ContentEncoding;
            public string CacheControl;
        }

        public static Headers For(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string name = fileName.ToLowerInvariant();
            string encoding = null;
            if (name.EndsWith(".gz")) { encoding = "gzip"; name = name.Substring(0, name.Length - 3); }
            else if (name.EndsWith(".br")) { encoding = "br"; name = name.Substring(0, name.Length - 3); }

            string type = null;
            if (name.EndsWith(".wasm")) type = "application/wasm";
            else if (name.EndsWith(".js")) type = "application/javascript";
            else if (name.EndsWith(".json")) type = "application/json";
            else if (name.EndsWith(".data") || name.EndsWith(".symbols") || name.EndsWith(".mem")) type = "application/octet-stream";
            else if (name.EndsWith(".html")) type = "text/html; charset=utf-8";

            if (encoding == null && type == null) return null;
            return new Headers
            {
                ContentType = type ?? "application/octet-stream",
                ContentEncoding = encoding,
                // index.html sempre fresco; os arquivos do build têm nome com hash e podem ficar em cache.
                CacheControl = name.EndsWith(".html") ? "no-cache" : "public, max-age=604800"
            };
        }
    }
}
