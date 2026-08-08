# Собирает ВЕСЬ Assembly-CSharp этого рабочего дерева без запуска Unity.
#
# Зачем. Офлайн-гейт (Tools/player-harness) видит только чистый слой данных;
# всё в Rendering/ не проверял никто, и опечатка в имени метода всплывала
# только когда ДМ открывал Unity. Этот скрипт закрывает дыру: настоящий
# компилятор, настоящие ссылки, весь код проекта разом.
#
# Как работает. Unity в корне проекта держит сгенерированный Assembly-CSharp.csproj
# со всеми ссылками на библиотеки. Мы берём его, заменяем поимённый список
# файлов на маску по Assets этого рабочего дерева и делаем относительные пути
# к библиотекам абсолютными — после этого проект собирается откуда угодно.
# Исходный проект открывается ТОЛЬКО на чтение.
#
# Требуется: Unity хотя бы раз открывал $SourceProject (иначе .csproj не создан).
#
# Пример:
#   pwsh Tools/compile-check/make-csproj.ps1
#   dotnet build (полученный путь) -v:m -nologo

param(
    # Проект, из которого берём готовый список ссылок.
    [string]$SourceProject = 'D:\D&D',
    # Рабочее дерево, исходники которого компилируем.
    [string]$Worktree = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    # Куда положить полученный проект (сборка пишет obj/bin рядом).
    [string]$OutDir = (Join-Path $env:TEMP 'unity-compile-check')
)

$srcCsproj = Join-Path $SourceProject 'Assembly-CSharp.csproj'
if (-not (Test-Path $srcCsproj)) {
    throw "Не найден $srcCsproj — Unity ни разу не открывал $SourceProject."
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force $OutDir | Out-Null }

# В XML амперсанд обязан быть записан как &amp; — иначе проект не разберётся.
$assetsGlob = (Join-Path $Worktree 'Assets\**\*.cs') -replace '&', '&amp;'
$sourceRoot = ((Resolve-Path $SourceProject).Path.TrimEnd('\') + '\') -replace '&', '&amp;'

$out = New-Object System.Collections.Generic.List[string]
$globInserted = $false
foreach ($line in (Get-Content $srcCsproj)) {
    # Поимённый список файлов заменяем одной маской: у Unity он от другой
    # ветки, там нет наших новых файлов и есть чужие.
    if ($line -match '<Compile Include') {
        if (-not $globInserted) {
            $out.Add('    <Compile Include="' + $assetsGlob + '" />')
            $globInserted = $true
        }
        continue
    }
    # None — шейдеры и текст, для компиляции не нужны, а пути у них относительные.
    if ($line -match '<None Include') { continue }
    # Относительный путь к библиотеке считается от папки проекта, а проект переехал.
    if ($line -match '<HintPath>(?![A-Za-z]:\\)') {
        $line = $line -replace '<HintPath>', ('<HintPath>' + $sourceRoot)
    }
    $out.Add($line)
}
if (-not $globInserted) { throw 'В исходном проекте нет ни одного <Compile Include> — форма файла изменилась.' }

$dst = Join-Path $OutDir 'CompileCheck.csproj'
Set-Content -Path $dst -Value $out -Encoding utf8
Write-Output $dst
