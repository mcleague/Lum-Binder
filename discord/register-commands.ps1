$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:DISCORD_APPLICATION_ID)) {
    throw 'Set DISCORD_APPLICATION_ID to the Discord application ID.'
}
if ([string]::IsNullOrWhiteSpace($env:DISCORD_BOT_TOKEN)) {
    throw 'Set DISCORD_BOT_TOKEN to the bot token. Do not share or commit it.'
}

$commands = @(
    @{
        name = 'submit'
        description = 'Отправить набор биндов на модерацию мастерской'
        options = @(
            @{ name = 'name'; description = 'Название набора'; type = 3; required = $true; max_length = 60 },
            @{ name = 'description'; description = 'Краткое описание набора'; type = 3; required = $true; max_length = 250 },
            @{
                name = 'type'
                description = 'Что содержит JSON-файл'
                type = 3
                required = $true
                choices = @(
                    @{ name = 'Целый профиль'; value = 'profile' },
                    @{ name = 'Набор биндов для выбранной вкладки'; value = 'binds' }
                )
            },
            @{ name = 'package'; description = 'JSON-файл профиля или биндов'; type = 11; required = $true }
        )
    },
    @{
        name = 'approve'
        description = 'Одобрить отправку мастерской и опубликовать её'
        options = @(
            @{ name = 'issue'; description = 'Номер заявки GitHub'; type = 4; required = $true; min_value = 1 }
        )
    }
)

$headers = @{
    Authorization = "Bot $env:DISCORD_BOT_TOKEN"
    'Content-Type' = 'application/json'
}
$body = ConvertTo-Json -InputObject $commands -Depth 10
$url = "https://discord.com/api/v10/applications/$env:DISCORD_APPLICATION_ID/commands"
Invoke-RestMethod -Method Put -Uri $url -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
Write-Output 'Discord workshop slash commands registered.'
