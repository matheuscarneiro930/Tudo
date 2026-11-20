using System.Runtime.CompilerServices;

// Permite que os testes automáticos montem baralhos com cartas definidas (cenários exatos).
[assembly: InternalsVisibleTo("Fodinha.Core.Tests")]

// Permite que a camada de rede reconstrua no cliente a MatchView recebida do servidor (os setters são internos
// para que a interface nunca altere a visão).
[assembly: InternalsVisibleTo("Fodinha.Net")]
