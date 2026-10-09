MEU CLAUDE — WIDGET PARA WINDOWS 11

<img width="268" height="58" alt="image" src="https://github.com/user-attachments/assets/9a3c3eec-0f31-4d52-b0d4-ea4023675f28" />

ATIVAR O NOVO RADAR (UMA VEZ)
1. Feche o widget antigo e extraia TODOS os arquivos deste pacote.
2. Abra Ativar-Radar.cmd, sem executar como administrador.
3. Reabra suas sessões do Claude Code no Windows e envie uma mensagem.
4. Abra Abrir-Widget.cmd. Seu login e preferências anteriores continuam salvos.

O arquivo Ativar-Radar.cmd adiciona hooks locais ao settings.json do Claude
Code do usuário. Faz backup antes de escrever e preserva outros hooks,
permissões e configurações. Reexecutá-lo não duplica esta integração.
Se você usa CLAUDE_CONFIG_DIR, ative a integração no mesmo ambiente/pasta
de configuração do Claude. Políticas que desativam hooks são respeitadas.
Não requer instalar extensão, Python, Node ou outro aplicativo.

O QUE APARECE
• Percentuais de uso existentes, com a política de atualização de 1 minuto.
• Nome do projeto e último estado observado do Claude Code.
• Arquivos alterados e linhas adicionadas/removidas no Git.
• Clique no nome do projeto para abrir a lista de sessões e escolher uma.
• “Seguir sessão mais recente” volta à seleção automática.
• No painel grande, clique no cartão de projeto para abrir os detalhes.
• Menu permite desligar avisos de conclusão e pedido de atenção.

CADÊNCIAS INDEPENDENTES
Uso da assinatura: a cada minuto, respeitando Retry-After do Claude.
Radar: leitura local dos eventos a cada 2 segundos.
Git do projeto selecionado: leitura local a cada 5 segundos, quando a
consulta anterior terminou. Não bloqueia a janela durante a leitura.

ESTADOS E LIMITAÇÕES DO RADAR
Trabalhando: prompt enviado ou atividade de ferramentas.
Precisa de você: pedido de permissão, AskUserQuestion ou notificação de entrada.
Resposta concluída: evento Stop. Não significa que a tarefa passou em testes.
Trabalho em segundo plano: Stop com tarefas de background ainda registradas.
Erro no turno: StopFailure. Sessão encerrada: SessionEnd.
Após 10 minutos sem evento em estados ativos, mostra “Sem evento recente”.
O painel lista sessões com evento nas últimas 24 horas (máximo 300).
Interromper com Ctrl+C ou matar o terminal pode não gerar evento de parada.
Por isso o radar mostra o último estado OBSERVADO e o horário do evento,
não uma garantia de que o processo continua vivo. Hooks de outros plugins
podem prolongar um turno depois de Stop; a próxima atividade atualiza o estado.
Subagentes não substituem o estado da sessão principal nesta versão.
Notificações breves entre duas leituras podem não aparecer no radar.
Avisos usam notificações do Windows; Não Perturbe pode ocultá-los.

GIT: O QUE ESTÁ SENDO CONTADO
Exige Git para Windows já disponível no PATH. Sem Git, o radar funciona,
mas informa que não conseguiu consultar o repositório.
Conta arquivos locais alterados, inclusive arquivos novos fora do índice.
+/− é o diff líquido dos arquivos rastreados contra HEAD: staged e unstaged
juntos, sem somar o mesmo trecho duas vezes. Linhas de arquivos untracked
não entram; binários e novos arquivos são indicados nos detalhes.
Antes do primeiro commit, mostra arquivos, mas não inventa totais de linhas.
Renomes contam como uma alteração. Mudanças em submódulos não são incluídas.
O resumo inclui alterações feitas por VOCÊ e pela IA: Git não identifica autor
de alterações ainda não commitadas. Nunca cria commit, descarta ou envia nada.
Branch e horário do último commit ficam nos detalhes do projeto.
Os nomes de arquivos, diffs e conteúdo de conversas não são enviados a ninguém.

DESATIVAR / DADOS LOCAIS
Execute Remover-Radar.cmd e reabra o Claude Code. Só os hooks deste radar
são removidos; os demais permanecem. É criado um backup da configuração.
O script do hook fica em %LOCALAPPDATA%\MeuClaudeWidget\RadarHook.ps1.
Os eventos ficam na subpasta radar. Contêm identificador derivado da sessão,
pasta do projeto, estado, evento, nome da ferramenta e horário. Não gravam
prompts, respostas, comandos executados, entradas/saídas de ferramentas,
transcrições nem tokens. Os dados só são lidos localmente.
Para apagar o histórico, com o widget fechado, apague apenas a subpasta radar.
Esta integração é para Claude Code NATIVO DO WINDOWS; não inclui WSL/SSH.

ATUALIZAR A VERSÃO QUE JÁ FUNCIONA
1. Feche o widget antigo pelo × ou pelo menu Sair junto ao relógio.
2. Extraia este ZIP e substitua os arquivos da pasta anterior.
3. Abra Abrir-Widget.cmd novamente.
O login já salvo em LocalAppData é reaproveitado. Não apague essa pasta.
O modo de exibição e a posição que você salvou são reaproveitados.
Se aparecer “já está aberto”, o widget antigo ainda precisa ser fechado.

VISUAL COMPACTO, INSPIRADO NO PRINT
• Faixa escura de 502 × 40 pixels em escala 100%, incluindo o radar.
• 5h em âmbar: uso da sessão. 7d em azul: uso da semana.
• Tempo de renovação logo abaixo; dados antigos ficam identificados.
• Posicionamento inicial perto da área do relógio, sobre a barra de tarefas.
• Arraste a faixa para ajustar o encaixe. A posição fica salva.
• Botão direito ou os três pontinhos abrem as opções.
• Duplo clique na faixa abre o painel completo, incluindo limites por modelo.
• No painel grande, clique em Compactar (canto inferior direito) para voltar.
• Duplo clique no título CLAUDE também alterna entre os dois modos.
• Passe o mouse para ver os detalhes e o horário da última leitura.
• Clique na área do projeto para selecionar outra sessão.
• Bolinha verde: conectado. Âmbar: sem dados ou leitura antiga.
• Menu “Alinhar perto do relógio”: reposiciona a faixa automaticamente.
• Menu “Modo compacto”: alterna entre a faixa e o painel grande.
• Menu “Sair”: fecha o widget.

É uma sobreposição sobre a barra, sem modificar o Explorer. Não reserva
espaço entre os ícones: se algum ficar por baixo, arraste a faixa para um
espaço livre. A posição automática varia com os ícones e a escala de tela.
Com a barra do Windows oculta automaticamente, o widget continua visível.
A opção de alinhamento automático usa a tela principal; você pode arrastar
para outra tela. Mantenha “por cima das outras janelas” ativado para a faixa.

COMEÇAR DO ZERO
1. Extraia a pasta inteira do ZIP para uma pasta sua.
2. Dê dois cliques em Abrir-Widget.cmd.
3. Na configuração, escolha a origem do login e clique em Salvar e conectar.

Não requer instalar Python, Node, extensão ou aplicativo de terceiros.
Usa Windows PowerShell 5.1, Windows Forms e .NET Framework do Windows 11.
O resumo de mudanças usa o Git para Windows, se já instalado.
Não execute como administrador. Não abra os arquivos de dentro do ZIP.
Na primeira abertura, a compilação em memória pode demorar alguns segundos.

OPÇÃO A — CLAUDE NO NAVEGADOR OU APP
Mesmo que você use o app desktop, entre na mesma conta pelo navegador:
1. Abra https://claude.ai e faça login normalmente.
2. Pressione F12 (ou Ctrl+Shift+I) no Edge/Chrome.
3. Abra a aba Application/Aplicativo. Pode estar escondida no menu >>.
4. Na lateral, abra Storage/Armazenamento > Cookies > https://claude.ai.
5. Localize o cookie chamado sessionKey. Copie SOMENTE seu valor completo.
6. No widget, escolha Navegador e cole no campo sessionKey.
7. Deixe Organização em branco. Se houver várias, você poderá escolher.

A chave dá acesso à sua sessão: não a envie por chat, e-mail ou print.
Ela é informada diretamente no programa local. Não cole código no console
nem instale extensões para extraí-la. O widget não lê seus outros cookies.
Se você trocar de conta, cole a nova chave e esvazie Organização.
Para trocar de organização da mesma conta, esvazie Organização e salve.
Se a sessão expirar, repita os passos para atualizar a chave.

OPÇÃO B — CLAUDE CODE JÁ INSTALADO
Escolha Claude Code e salve. O widget lê o login local existente em:
%USERPROFILE%\.claude\.credentials.json
Se CLAUDE_CONFIG_DIR estiver definida, usa a pasta indicada por ela.
O arquivo é apenas lido. O widget não escreve nem renova esses tokens.
Se expirar, abra o Claude Code e renove o login normalmente.
Esta opção exige login de assinatura Claude, não uma chave de API.
O login do app Claude Desktop e o login dentro de WSL não são lidos.
Se você usa somente WSL, use a opção Navegador.
Ao trocar a conta no Claude Code, o widget acompanhará o login local atual.

USAR O WIDGET
• Arraste o título ou uma área vazia para posicionar no canto da tela.
• Exibe sessão de 5h, semana geral e limites por modelo quando retornados.
• A posição é salva; se o monitor sumir, volta à tela principal ao reabrir.
• Atualização automática: um ciclo de consulta a cada 60 segundos.
• A configuração antiga de intervalo é ajustada para 60 segundos ao abrir.
• Atualizar consulta quando o intervalo terminou; respeita pausas do servidor.
• Configurar permite mudar a conexão e a opção sempre por cima.
• Clique no status para ver uma dica temporária, sem janela bloqueante.
• Botão direito ou ícone junto ao relógio: página de uso, desconectar e sair.
• O × do painel completo ou Sair no menu fecha o programa.
• A janela não ocupa um botão na barra de tarefas.
• Uma segunda abertura avisa que o widget já está funcionando.

ABRIR JUNTO COM O WINDOWS (OPCIONAL)
1. Crie um atalho de Abrir-Widget.cmd.
2. Pressione Win+R, digite shell:startup e confirme.
3. Coloque somente o atalho nessa pasta. Remova-o para desativar.
Não mude de lugar a pasta do widget depois de criar o atalho.

PRIVACIDADE E FUNCIONAMENTO
Código próprio em Widget.cs e Radar.cs. Iniciar.ps1 compila-os em memória.
Ativar-Radar.cmd é a única etapa que altera a configuração do Claude Code.
Não baixa pacotes, não instala serviços, não altera o registro e não envia
telemetria. Usa requisições GET HTTPS para dados de uso, sem gerar mensagens.
Destinos de rede programados:
• https://claude.ai/api/organizations
• https://claude.ai/api/organizations/{uuid}/usage
• https://api.anthropic.com/api/oauth/usage
O botão de uso abre https://claude.ai/settings/usage no navegador padrão.
Redirecionamentos HTTP não são seguidos; certificados TLS são verificados.
A proteção do Windows (DPAPI, CurrentUser) criptografa a chave de sessão
salva para seu usuário. Não protege contra programas maliciosos executados
como você. Nenhuma credencial real está incluída neste pacote.
Preferências e chave protegida ficam em %LOCALAPPDATA%\MeuClaudeWidget.
No modo Claude Code, o widget não guarda cópia do token no seu config.json.
Os lançadores usam ExecutionPolicy Bypass só no processo iniciado, sem
alterar permanentemente a política do Windows. Políticas corporativas
podem impedir a execução; não é necessário desativar proteções do sistema.

LIMITAÇÕES REAIS
O usuário confirmou que a conexão das versões anteriores funcionou.
A versão 2.0 adiciona radar e Git. Os comandos Git foram verificados em
repositório temporário (staged, unstaged, renome, arquivo novo e sem commit).
C#, interface WinForms e hooks PowerShell não puderam ser executados neste
ambiente Linux. Incluí testes locais para compilação e integração isolada.
Não é um aplicativo oficial Anthropic. Os endpoints de uso são internos,
podem mudar e podem recusar chamadas fora do site/app, inclusive com 403
ou 429. Um cookie válido não garante que o acesso seja aceito.
Não tenta contornar bloqueios. Se ocorrer 403, confira o login na página
oficial; se persistir, use o modo Claude Code se você já o utiliza.
A atualização é periódica: há até um intervalo de atraso, além do atraso
que o próprio Claude tiver para atualizar suas métricas. Não há push.
Percentuais são os retornados pelo serviço; não são estimados por tokens.
Dados ausentes aparecem como —, nunca como 0%. Dados antigos ficam cinza,
com aviso e horário da última leitura. O fim do prazo não zera o uso local.
Não há mais espera local de 5 minutos nem aumento progressivo por falhas.
Sem um prazo do servidor, inclusive após 429, tenta novamente no próximo
ciclo de 60 segundos. O tempo gasto na consulta conta nesse minuto.
Se o servidor enviar Retry-After, esse prazo é respeitado. Uma recusa 429
não pode ser removida pelo widget: nesse período não há percentual novo.
Nenhuma janela de aviso de conexão é aberta; os detalhes ficam na dica.
Os últimos valores continuam marcados como antigos até uma leitura válida.
Não há requisições paralelas: consultas em andamento terminam primeiro.
O widget acompanha franquia de assinatura. Não mede gastos de API,
compras de créditos nem tamanho da janela de contexto.
Uma janela sempre por cima não se sobrepõe à tela de bloqueio, UAC nem a
todos os jogos em tela cheia exclusiva.

TESTE LOCAL OPCIONAL
Abra Testar-Widget.cmd. Ele compila o mesmo código e verifica tratamento
de zero, valores ausentes, limites por modelo, datas, respostas inválidas,
valores numéricos, proteção DPAPI e interpretação de dados Git. Não lê a conta nem faz requisições.
Testar-Radar.cmd verifica os hooks e a instalação/remoção em pasta
temporária, sem tocar na sua configuração real do Claude.
Os testes não validam conexão real, aparência ou comportamento de DPI.
Em caso de erro ao abrir, copie a mensagem da janela (sem suas chaves).

REMOVER
Execute Remover-Radar.cmd primeiro. Feche o widget. Apague a pasta extraída e, se existir, o atalho de inicialização.
Apague %LOCALAPPDATA%\MeuClaudeWidget para remover as preferências e a
chave protegida. Desconectar também remove a chave da configuração, mas
não revoga a sessão no Claude. Encerrar sessões é feito na própria conta.

ARQUIVOS
Abrir-Widget.cmd   — iniciar com dois cliques
Iniciar.ps1        — carregador usando componentes do Windows
Widget.cs          — fonte completo em C#
Testar-Widget.cmd  — testes locais opcionais
LEIA-ME.txt        — este guia

NOVOS ARQUIVOS
Radar.cs — sessões, leitura Git e painel de detalhes
RadarHook.ps1 — receptor de eventos locais
Integracao-Radar.ps1 — ativação/remoção com backup
Ativar-Radar.cmd / Remover-Radar.cmd — atalhos da integração
Testar-Radar.cmd / Testar-Radar.ps1 — testes em ambiente isolado
