# 🚓 SIAD • Gestão de Frota

Aplicação desktop moderna desenvolvida em **.NET 8 + WPF (Material
Design)** para automação de consultas e lançamentos no sistema **SIAD --
Frota de Veículos (MG)**.

------------------------------------------------------------------------

## ✨ Funcionalidades

### 🔐 Login

-   Usuário
-   Senha (não armazenada em disco)
-   Unidade Processadora (carregada de JSON)

------------------------------------------------------------------------

### 🔎 Consulta de Viaturas

-   Consulta individual
-   Consulta de todas as viaturas cadastradas
-   Extração automática de:
    -   🚘 Placa
    -   📍 Hodômetro
    -   ⏰ Hora
    -   📄 REDS
    -   👤 Condutor

#### 🎨 Colorização automática da tabela

  Situação                   Cor
  -------------------------- -------------
  Retorno completo           🟢 Verde
  Sem REDS                   🟡 Amarelo
  Sem REDS + sem Hodômetro   🔴 Vermelho

------------------------------------------------------------------------

### 🚗 Lançamentos

#### ➕ Saída

-   Seleção automática de motorista por CPF
-   Preenchimento automático de data/hora
-   Confirmação automática

#### ⬅️ Retorno

-   Preenchimento de hodômetro
-   Registro de REDS
-   Confirmação automática

------------------------------------------------------------------------

### ❌ Exclusão do Último Lançamento

Disponível via:

-   Clique direito na linha da consulta → **Deletar lançamento**

Fluxo automatizado:

Login → Módulo Frota → 6 → 2 → 3 → Placa → Enter → S + Enter

Inclui tratamento automático de confirmações (POS39 / POS91).

------------------------------------------------------------------------

## 📂 Estrutura do Projeto

SiadFrotaDesktop/ │ ├── Models/ ├── Services/ │ ├── SiadClient.cs │ └──
HtmlFormHelper.cs │ ├── Data/ │ ├── unidades.json │ ├── frota.json │ └──
motoristas.json │ ├── MainWindow.xaml └── MainWindow.xaml.cs

------------------------------------------------------------------------

## 📁 Arquivos de Configuração

### Data/unidades.json

\[ { "Codigo": "1401670", "Nome": "FORMIGA" }\]

### Data/frota.json

Lista de viaturas monitoradas.

### Data/motoristas.json

Lista de motoristas com CPF e nome.

------------------------------------------------------------------------

## 🛠 Tecnologias Utilizadas

-   .NET 8
-   WPF
-   MaterialDesignInXamlToolkit
-   HttpClient + CookieContainer
-   AngleSharp (HTML Parser)

------------------------------------------------------------------------

## ▶️ Executar Projeto

dotnet clean\
dotnet build\
dotnet run

------------------------------------------------------------------------

## ⚠️ Observações Técnicas

-   Não utiliza Selenium
-   Não utiliza automação de teclado
-   Comunicação direta via HTTP
-   Baseado na navegação interna do GeneXus (GX_SeqScreenNumber)

------------------------------------------------------------------------

## 📌 Objetivo

Automatizar operações repetitivas no SIAD, reduzindo tempo operacional e
minimizando erros manuais.

------------------------------------------------------------------------

© 2026 • Projeto interno de automação
