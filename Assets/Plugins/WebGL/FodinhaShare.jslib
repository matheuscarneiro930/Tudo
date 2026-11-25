// Compartilhar texto na versão web: menu do navegador (navigator.share, ex.: Safari do iPhone) ou copia o texto.
mergeInto(LibraryManager.library, {
  // Retorna 1 = abriu o menu de compartilhar, 2 = copiou o texto, 0 = não deu.
  FodinhaShare_Text: function (textPtr) {
    var text = UTF8ToString(textPtr);
    try {
      if (navigator.share) {
        navigator.share({ text: text }).catch(function () { });
        return 1;
      }
    } catch (e) { }
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text);
        return 2;
      }
    } catch (e) { }
    try {
      var area = document.createElement('textarea');
      area.value = text;
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      var ok = document.execCommand('copy');
      document.body.removeChild(area);
      return ok ? 2 : 0;
    } catch (e) { }
    return 0;
  }
});
