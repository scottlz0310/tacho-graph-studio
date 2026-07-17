# Pester 5+ ではトップレベル変数は Run フェーズの It から見えないため BeforeAll で定義する
BeforeAll {
    $scriptPath = (Resolve-Path "$PSScriptRoot/../scripts/Install-TachoGraphStudio.ps1").Path
}

Describe "Install-TachoGraphStudio.ps1" {
    Context "非管理者権限で実行された場合" {
        It "UAC昇格後の子プロセスが正常終了(0)したとき、親プロセスも0を返すこと" {
            $principalMock = [PSCustomObject]@{ Identity = $null }
            $principalMock | Add-Member -MemberType ScriptMethod -Name IsInRole -Value { return $false } -Force
            
            $startProcessOverride = { return [PSCustomObject]@{ ExitCode = 0 } }
            
            $res = & $scriptPath -Test -PrincipalOverride $principalMock -StartProcessOverride $startProcessOverride
            $res | Should -Be 0
        }
        
        It "UAC昇格後の子プロセスが異常終了(1)したとき、親プロセスも1を返すこと" {
            $principalMock = [PSCustomObject]@{ Identity = $null }
            $principalMock | Add-Member -MemberType ScriptMethod -Name IsInRole -Value { return $false } -Force
            
            $startProcessOverride = { return [PSCustomObject]@{ ExitCode = 1 } }
            
            $res = & $scriptPath -Test -PrincipalOverride $principalMock -StartProcessOverride $startProcessOverride
            $res | Should -Be 1
        }

        It "UAC昇格がユーザーによりキャンセルされたとき、親プロセスは1を返すこと" {
            $principalMock = [PSCustomObject]@{ Identity = $null }
            $principalMock | Add-Member -MemberType ScriptMethod -Name IsInRole -Value { return $false } -Force
            
            $startProcessOverride = { throw "The operation was canceled by the user" }
            
            $res = & $scriptPath -Test -PrincipalOverride $principalMock -StartProcessOverride $startProcessOverride
            $res | Should -Be 1
        }
    }

    Context "管理者権限で実行された場合" {
        It "管理者権限がある場合、インポートとインストール（テストモード）が正常に完了し、0を返すこと" {
            $principalMock = [PSCustomObject]@{ Identity = $null }
            $principalMock | Add-Member -MemberType ScriptMethod -Name IsInRole -Value { return $true } -Force
            
            Mock Invoke-WebRequest { }
            
            $res = & $scriptPath -Test -PrincipalOverride $principalMock
            $res | Should -Be 0
        }
    }
}
