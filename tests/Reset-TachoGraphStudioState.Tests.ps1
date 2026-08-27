# Pester 5+ ではトップレベル変数は Run フェーズの It から見えないため BeforeAll で定義する
BeforeAll {
    $scriptPath = (Resolve-Path "$PSScriptRoot/../scripts/Reset-TachoGraphStudioState.ps1").Path

    function New-FakePackageRoot {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
        foreach ($relative in @("LocalState\settings", "LocalState\templates", "LocalCache\secrets", "LocalCache\roster")) {
            $directory = Join-Path $root $relative
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $directory "dummy.json") -Value "{}"
        }
        return $root
    }

    $noProcess = { return $null }
}

Describe "Reset-TachoGraphStudioState.ps1" {
    Context "スコープ指定" {
        It "-Scope <Scope> は <Remaining> を残して削除すること" -ForEach @(
            @{ Scope = @("Settings");           Removed = @("LocalState\settings");                        Remaining = @("LocalState\templates", "LocalCache\secrets", "LocalCache\roster") }
            @{ Scope = @("Templates");          Removed = @("LocalState\templates");                       Remaining = @("LocalState\settings", "LocalCache\secrets", "LocalCache\roster") }
            @{ Scope = @("Secrets");            Removed = @("LocalCache\secrets");                         Remaining = @("LocalState\settings", "LocalState\templates", "LocalCache\roster") }
            @{ Scope = @("Cache");              Removed = @("LocalCache\roster");                          Remaining = @("LocalState\settings", "LocalState\templates", "LocalCache\secrets") }
            @{ Scope = @("Secrets", "Cache");   Removed = @("LocalCache\secrets", "LocalCache\roster");    Remaining = @("LocalState\settings", "LocalState\templates") }
            @{ Scope = @("All");                Removed = @("LocalState\settings", "LocalState\templates", "LocalCache\secrets", "LocalCache\roster"); Remaining = @() }
        ) {
            $root = New-FakePackageRoot
            try {
                $res = & $scriptPath -Scope $Scope -PackageRoot $root -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 0

                foreach ($relative in $Removed) {
                    Test-Path -LiteralPath (Join-Path $root $relative) | Should -BeFalse
                }
                foreach ($relative in $Remaining) {
                    Test-Path -LiteralPath (Join-Path $root $relative) | Should -BeTrue
                }
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "重複したスコープを指定しても正常終了すること" {
            $root = New-FakePackageRoot
            try {
                $res = & $scriptPath -Scope @("Secrets", "Secrets") -PackageRoot $root -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 0
                Test-Path -LiteralPath (Join-Path $root "LocalCache\secrets") | Should -BeFalse
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "削除を行わない場合" {
        It "-WhatIf を指定したとき、何も削除せず 0 を返すこと" {
            $root = New-FakePackageRoot
            try {
                $res = & $scriptPath -PackageRoot $root -Test -GetProcessOverride $noProcess -WhatIf
                $res | Should -Be 0
                Test-Path -LiteralPath (Join-Path $root "LocalState\settings") | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $root "LocalCache\secrets") | Should -BeTrue
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "アプリが起動中のとき、削除せず 1 を返すこと" {
            $root = New-FakePackageRoot
            try {
                $runningProcess = { return [PSCustomObject]@{ Id = 1234 } }
                $res = & $scriptPath -PackageRoot $root -Test -GetProcessOverride $runningProcess -Confirm:$false
                $res | Should -Be 1
                Test-Path -LiteralPath (Join-Path $root "LocalState\settings") | Should -BeTrue
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "アプリデータフォルダが存在しないとき、1 を返すこと" {
            $missing = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            $res = & $scriptPath -PackageRoot $missing -Test -GetProcessOverride $noProcess -Confirm:$false
            $res | Should -Be 1
        }

        It "対象フォルダが存在しないときはスキップして 0 を返すこと" {
            $root = New-FakePackageRoot
            try {
                Remove-Item -LiteralPath (Join-Path $root "LocalCache\secrets") -Recurse -Force
                $res = & $scriptPath -Scope @("Secrets") -PackageRoot $root -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 0
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "アプリデータフォルダの解決" {
        It "-PackageRoot 省略時、<PackagesRoot> 配下の <PackageName>_* を対象にすること" {
            $packages = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            $root = Join-Path $packages "TachoGraphStudio_cg40bsw2fqbmr"
            try {
                New-Item -ItemType Directory -Path (Join-Path $root "LocalCache\secrets") -Force | Out-Null
                $res = & $scriptPath -Scope @("Secrets") -PackagesRoot $packages -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 0
                Test-Path -LiteralPath (Join-Path $root "LocalCache\secrets") | Should -BeFalse
            }
            finally {
                Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "候補が見つからないとき、1 を返すこと" {
            $packages = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            New-Item -ItemType Directory -Path $packages -Force | Out-Null
            try {
                $res = & $scriptPath -PackagesRoot $packages -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 1
            }
            finally {
                Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "候補が複数あるとき、削除せず 1 を返すこと" {
            $packages = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            try {
                foreach ($suffix in @("aaaaaaaaaaaaa", "bbbbbbbbbbbbb")) {
                    New-Item -ItemType Directory -Path (Join-Path $packages "TachoGraphStudio_$suffix\LocalCache\secrets") -Force | Out-Null
                }
                $res = & $scriptPath -PackagesRoot $packages -Test -GetProcessOverride $noProcess -Confirm:$false
                $res | Should -Be 1
                Test-Path -LiteralPath (Join-Path $packages "TachoGraphStudio_aaaaaaaaaaaaa\LocalCache\secrets") | Should -BeTrue
            }
            finally {
                Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
