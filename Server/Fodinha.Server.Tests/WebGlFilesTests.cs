using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    public class WebGlFilesTests
    {
        [TestCase("Jogo.wasm.gz", "application/wasm", "gzip")]
        [TestCase("Jogo.framework.js.gz", "application/javascript", "gzip")]
        [TestCase("Jogo.data.gz", "application/octet-stream", "gzip")]
        [TestCase("Jogo.wasm.br", "application/wasm", "br")]
        [TestCase("Jogo.loader.js", "application/javascript", null)]
        [TestCase("index.html", "text/html; charset=utf-8", null)]
        public void CabecalhosDoBuildWeb(string file, string type, string encoding)
        {
            var h = WebGlFiles.For(file);
            Assert.That(h, Is.Not.Null);
            Assert.That(h.ContentType, Is.EqualTo(type));
            Assert.That(h.ContentEncoding, Is.EqualTo(encoding));
        }

        [Test]
        public void IndexNaoFicaEmCache_BuildFica()
        {
            Assert.That(WebGlFiles.For("index.html").CacheControl, Is.EqualTo("no-cache"));
            Assert.That(WebGlFiles.For("abc123.wasm.gz").CacheControl, Does.Contain("max-age"));
        }

        [Test]
        public void ArquivoComum_UsaPadrao()
        {
            Assert.That(WebGlFiles.For("icone.png"), Is.Null);
        }
    }
}
