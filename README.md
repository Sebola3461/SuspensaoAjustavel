# Suspensão Ajustável

Suspensão ajustável a ar para os ônibus do Proton Bus Simulator.

- **Ajoelhar:** baixa o lado das portas para o embarque. Liga sozinho quando o ônibus está parado com a porta aberta,
  se a opção `AutoAjoelhar` estiver ativa.
- **Elevar:** sobe o ônibus para passar em valetas e lombadas.
- **Altura por eixo (par de bolsas) e rigidez por ônibus:** cada eixo — dianteiro, do meio (articulados e biarticulados) e traseiro — tem a sua altura, salva em `UserData/SuspensaoAjustavel/<ônibus>.txt`.
- **Reservatório de ar:** é o mesmo do freio e das portas, simulado pela PBSLoaderCommonUtils. Sem a simulação de ar dela, a
  SuspensaoAjustavel usa um reservatório próprio, que também só enche com o motor ligado.
- **Sem ar:** abaixo de 4,5 bar, as bolsas vão murchando devagar até o ônibus sentar no batente (`AlturaSemAr`,
  `VelocidadeSemAr`), e aparece o aviso "Suspensão sem ar". Quando o compressor passa de 5 bar, elas enchem de novo,
  gastando ar.
- **Travas:** o ônibus sai do ajoelhamento sozinho acima de 5 km/h e da elevação acima de 30 km/h.
- **Som de ar:** toca ao esvaziar e ao encher as bolsas.

## Teclas

| Tecla | Ação |
|---|---|
| Ctrl+Alt+F3 | Abre o painel |
| Ctrl+Alt+Shift+F3 | Grava `UserData/SuspensaoAjustavel/diagnostico.txt` |
| Page Down | Ajoelha, ou volta ao normal |
| Page Up | Eleva, ou volta ao normal |

## Opções

As opções ficam na categoria `[SuspensaoAjustavel]`: lado do ajoelhamento, quanto cada eixo baixa, velocidade das bolsas,
limites de velocidade.

## Problemas conhecidos

O som de ar não passa pelo mixer do jogo, então o controle de volume do jogo pode não afetá-lo.

## Desenvolvimento

`--pbsloadercommoninterface-partida --pbsloadercommoninterface-onibus=1519 --suspensao-teste --suspensao-sair` mede a altura de cada canto do ônibus
em cada nível e grava `teste.txt`.
