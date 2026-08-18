[CmdletBinding()]
param(
    [Parameter()]
    [string] $BaseUrl = 'https://wms2api.v3rii.com',

    [Parameter()]
    [string] $Identifier = $env:WMS_TEST_IDENTIFIER,

    [Parameter()]
    [string] $Password = $env:WMS_TEST_PASSWORD,

    [Parameter()]
    [string] $BranchCode = '0',

    [Parameter()]
    [ValidateRange(10, 500)]
    [int] $PageSize = 100,

    [Parameter()]
    [ValidateRange(5, 60)]
    [int] $RequestTimeoutSeconds = 15,

    [Parameter()]
    [string] $EndpointPattern = '*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Identifier) -or [string]::IsNullOrWhiteSpace($Password)) {
    throw 'WMS_TEST_IDENTIFIER ve WMS_TEST_PASSWORD ortam değişkenleri gereklidir.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$turkishPattern = '[çÇğĞıİöÖşŞüÜâÂîÎûÛ]'
$semanticFieldPattern = '(?i)(name|description|subject|title|code|no|email|city|district|address|type|status|reason|source|reference)'
$ignoredFields = @('rowVersion', 'branchCode')

function Get-PropertyValue {
    param([object] $InputObject, [string] $Name)

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-AsciiTurkish {
    param([string] $Value)

    return $Value.
        Replace('Ç', 'C').Replace('ç', 'c').
        Replace('Ğ', 'G').Replace('ğ', 'g').
        Replace('İ', 'I').Replace('ı', 'i').
        Replace('Ö', 'O').Replace('ö', 'o').
        Replace('Ş', 'S').Replace('ş', 's').
        Replace('Ü', 'U').Replace('ü', 'u').
        Replace('Â', 'A').Replace('â', 'a').
        Replace('Î', 'I').Replace('î', 'i').
        Replace('Û', 'U').Replace('û', 'u')
}

function Get-HttpFailure {
    param([System.Management.Automation.ErrorRecord] $Record)

    $status = 0
    $responseProperty = $Record.Exception.PSObject.Properties['Response']
    if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
        try { $status = [int] $responseProperty.Value.StatusCode } catch { $status = 0 }
    }

    $errorDetailsProperty = $Record.PSObject.Properties['ErrorDetails']
    $errorMessage = if ($null -ne $errorDetailsProperty -and $null -ne $errorDetailsProperty.Value) {
        $messageProperty = $errorDetailsProperty.Value.PSObject.Properties['Message']
        if ($null -ne $messageProperty) { [string] $messageProperty.Value } else { $null }
    } else { $null }
    $message = if (-not [string]::IsNullOrWhiteSpace($errorMessage)) {
        $errorMessage
    } else {
        $Record.Exception.GetBaseException().Message
    }

    return [pscustomobject]@{ Ok = $false; Status = $status; Data = $null; Error = $message }
}

function Invoke-WmsPost {
    param(
        [string] $Path,
        [object] $Body,
        [hashtable] $Headers
    )

    try {
        $requestParameters = @{
            Method = 'Post'
            Uri = $BaseUrl.TrimEnd('/') + $Path
            Headers = $Headers
            ContentType = 'application/json'
            Body = $Body | ConvertTo-Json -Depth 20 -Compress
            TimeoutSec = $RequestTimeoutSeconds
        }
        $response = Invoke-RestMethod @requestParameters
        return [pscustomobject]@{ Ok = $true; Status = 200; Data = $response; Error = $null }
    } catch {
        return Get-HttpFailure $_
    }
}

function New-PagedBody {
    param([AllowNull()][string] $Search, [AllowNull()][string] $Field)

    [string[]] $searchFields = @()
    if (-not [string]::IsNullOrWhiteSpace($Field)) { $searchFields = @($Field) }

    return @{
        pageNumber = 1
        pageSize = $PageSize
        search = $Search
        searchFields = $searchFields
        sortDirection = 'asc'
        filterLogic = 'and'
        filters = @()
    }
}

function Get-PageData {
    param([object] $Response)

    $envelope = Get-PropertyValue $Response 'data'
    if ($null -eq $envelope) { $envelope = $Response }

    $items = Get-PropertyValue $envelope 'items'
    if ($null -eq $items) { $items = Get-PropertyValue $envelope 'data' }
    if ($null -eq $items) { $items = @() }

    $total = Get-PropertyValue $envelope 'totalCount'
    if ($null -eq $total) { $total = @($items).Count }

    return [pscustomobject]@{
        Items = @($items)
        TotalCount = [long] $total
    }
}

function Get-SearchCandidates {
    param([object[]] $Items, [bool] $RequireTurkish)

    $candidates = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $Items) {
        foreach ($property in $row.PSObject.Properties) {
            if ($ignoredFields -contains $property.Name -or $property.Value -isnot [string]) { continue }
            $value = $property.Value.Trim()
            if ($value.Length -lt 2 -or $value.Length -gt 180) { continue }

            $hasTurkish = $value -cmatch $turkishPattern
            if ($RequireTurkish -ne $hasTurkish) { continue }
            if (-not $hasTurkish -and $value -notmatch '[A-Za-z]{2}') { continue }

            $score = 0
            if ($property.Name -match $semanticFieldPattern) { $score += 100 }
            if ($value.Length -ge 4 -and $value.Length -le 80) { $score += 20 }
            if ($value -match '\s') { $score += 5 }
            $candidates.Add([pscustomobject]@{
                Field = $property.Name
                Value = $value
                Row = $row
                Score = $score
            })
        }
    }

    return @($candidates | Sort-Object Score -Descending | Select-Object -First 5)
}

function Get-RowIdentity {
    param([object] $Row, [string] $Field)

    $id = Get-PropertyValue $Row 'id'
    if ($null -ne $id) { return "id:$id" }
    return "${Field}:$(Get-PropertyValue $Row $Field)"
}

function Get-SearchTerm {
    param([string] $Value, [ValidateSet('Turkish', 'English')][string] $Kind)

    $tokens = @([regex]::Matches($Value, '[\p{L}\p{Nd}._/-]+') | ForEach-Object { $_.Value })
    if ($Kind -eq 'Turkish') {
        $turkishTokens = @($tokens | Where-Object { $_ -cmatch $turkishPattern -and $_.Length -ge 2 } | Sort-Object Length -Descending)
        if ($turkishTokens.Count -gt 0) { return $turkishTokens[0] }
    }

    if ($tokens.Count -le 5 -and $Value.Length -le 80) { return $Value }
    $asciiTokens = @($tokens | Where-Object { $_ -match '[A-Za-z]{2}' } | Sort-Object Length -Descending)
    if ($asciiTokens.Count -gt 0) { return $asciiTokens[0] }
    return $Value
}

function ConvertTo-AlternatingCase {
    param(
        [string] $Value,
        [bool] $StartUpper,
        [Globalization.CultureInfo] $Culture
    )

    $builder = [Text.StringBuilder]::new($Value.Length)
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $text = [string] $Value[$index]
        $upper = if ($index % 2 -eq 0) { $StartUpper } else { -not $StartUpper }
        [void] $builder.Append($(if ($upper) { $Culture.TextInfo.ToUpper($text) } else { $Culture.TextInfo.ToLower($text) }))
    }
    return $builder.ToString()
}

function Get-SearchVariants {
    param([string] $Value, [ValidateSet('Turkish', 'English')][string] $Kind)

    $turkishCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')
    $ascii = ConvertTo-AsciiTurkish $Value
    $candidates = if ($Kind -eq 'Turkish') {
        @(
            $Value,
            $ascii,
            $Value.ToUpperInvariant(),
            $Value.ToLowerInvariant(),
            $turkishCulture.TextInfo.ToUpper($Value),
            $turkishCulture.TextInfo.ToLower($Value),
            $ascii.ToUpperInvariant(),
            $ascii.ToLowerInvariant(),
            (ConvertTo-AlternatingCase $Value $true $turkishCulture),
            (ConvertTo-AlternatingCase $Value $false $turkishCulture),
            (ConvertTo-AlternatingCase $ascii $true ([Globalization.CultureInfo]::InvariantCulture)),
            (ConvertTo-AlternatingCase $ascii $false ([Globalization.CultureInfo]::InvariantCulture))
        )
    } else {
        @(
            $Value,
            $Value.ToUpperInvariant(),
            $Value.ToLowerInvariant(),
            (ConvertTo-AlternatingCase $Value $true ([Globalization.CultureInfo]::InvariantCulture)),
            (ConvertTo-AlternatingCase $Value $false ([Globalization.CultureInfo]::InvariantCulture))
        )
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    return @($candidates | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $seen.Add($_)
    })
}

function Test-ContainsTarget {
    param([object[]] $Items, [string] $Identity, [string] $Field)

    foreach ($row in $Items) {
        if ((Get-RowIdentity $row $Field) -eq $Identity) { return $true }
    }
    return $false
}

function Get-IdentitySet {
    param([object[]] $Items, [string] $Field)

    return @($Items | ForEach-Object { Get-RowIdentity $_ $Field } | Sort-Object -Unique)
}

function Test-SearchVariants {
    param(
        [string] $Path,
        [object[]] $Candidates,
        [hashtable] $Headers,
        [ValidateSet('Turkish', 'English')]
        [string] $Kind
    )

    foreach ($candidate in $Candidates) {
        $searchTerm = Get-SearchTerm $candidate.Value $Kind
        $variants = @(Get-SearchVariants $searchTerm $Kind)
        if ($variants.Count -lt 2) { continue }

        $identity = Get-RowIdentity $candidate.Row $candidate.Field
        $referenceSet = $null
        $referenceTotal = $null
        $allHaveTarget = $true
        $allSetsEqual = $true
        $totals = [Collections.Generic.List[long]]::new()
        $unsupported = $false

        foreach ($variant in $variants) {
            $response = Invoke-WmsPost $Path (New-PagedBody $variant $candidate.Field) $Headers
            if (-not $response.Ok) {
                if ($response.Status -eq 400) { $unsupported = $true; break }
                return [pscustomobject]@{
                    Status = 'HTTP_ERROR'; Field = $candidate.Field; Search = $searchTerm
                    Alternate = ($variants -join ' | '); VariantCount = $variants.Count; Detail = $response.Error
                }
            }

            $variantPage = Get-PageData $response.Data
            $variantSet = @(Get-IdentitySet $variantPage.Items $candidate.Field)
            $totals.Add($variantPage.TotalCount)
            if (-not (Test-ContainsTarget $variantPage.Items $identity $candidate.Field)) { $allHaveTarget = $false }

            if ($null -eq $referenceSet) {
                $referenceSet = $variantSet
                $referenceTotal = $variantPage.TotalCount
            } elseif ($referenceTotal -ne $variantPage.TotalCount -or $null -ne (Compare-Object $referenceSet $variantSet)) {
                $allSetsEqual = $false
            }
        }

        if ($unsupported) { continue }

        return [pscustomobject]@{
            Status = if ($allHaveTarget -and $allSetsEqual) { 'PASS' } else { 'MISMATCH' }
            Field = $candidate.Field
            Search = $searchTerm
            Alternate = ($variants -join ' | ')
            VariantCount = $variants.Count
            Detail = "variants=$($variants.Count) targetAll=$allHaveTarget totals=$(@($totals | Sort-Object -Unique) -join ',') setEqual=$allSetsEqual"
        }
    }

    return [pscustomobject]@{ Status = 'NO_SUPPORTED_FIELD'; Field = $null; Search = $null; Alternate = $null; VariantCount = 0; Detail = 'Aranabilir örnek alan bulunamadı veya endpoint alanı reddetti.' }
}

function Get-PagedEndpoints {
    $endpoints = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem (Join-Path $repositoryRoot 'Modules') -Recurse -Filter '*Controller.cs') {
        $source = [IO.File]::ReadAllText($file.FullName)
        $controllerRoute = [regex]::Match($source, 'Route\("(api/[^\"]+)"\)').Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($controllerRoute)) { continue }

        foreach ($match in [regex]::Matches($source, 'HttpPost\("([^\"]*paged[^\"]*)"\)')) {
            $endpoints.Add('/' + $controllerRoute.TrimEnd('/') + '/' + $match.Groups[1].Value.TrimStart('/'))
        }
    }

    $expanded = [System.Collections.Generic.List[string]]::new()
    foreach ($endpoint in $endpoints | Sort-Object -Unique) {
        if ($endpoint.Contains('{documentType}')) {
            foreach ($type in @('request', 'rfq', 'quote', 'order')) { $expanded.Add($endpoint.Replace('{documentType}', $type)) }
            continue
        }
        if ($endpoint.Contains('{direction}')) {
            foreach ($direction in @('IssueToSupplier', 'ReceiptFromSupplier')) { $expanded.Add($endpoint.Replace('{direction}', $direction)) }
            continue
        }
        if ($endpoint.Contains('{id:long}')) {
            $expanded.Add($endpoint.Replace('{id:long}', '__SERIAL_ID__'))
            continue
        }
        $expanded.Add($endpoint)
    }

    return @($expanded | Sort-Object -Unique | ForEach-Object {
        if ($_ -eq '/api/quality/rules/stock-groups/paged') { "$_`?branchCode=$BranchCode" }
        elseif ($_ -eq '/api/quality/decision-codes/options/paged') { "$_`?branchCode=$BranchCode&decision=2" }
        elseif ($_ -eq '/api/steel-receipts/vehicle-acceptance/candidates/paged') { "$_`?branchCode=$BranchCode" }
        elseif ($_ -in @(
            '/api/goods-receipts/supplier-stock-mappings/paged',
            '/api/incoming-invoices/connections/paged',
            '/api/incoming-invoices/paged'
        )) { "$_`?branchCode=$BranchCode" }
        else { $_ }
    })
}

$login = Invoke-WmsPost '/api/auth/login' @{
    identifier = $Identifier
    password = $Password
    branchCode = $BranchCode
} @{}
if (-not $login.Ok) { throw "Login başarısız: $($login.Error)" }

$loginData = Get-PropertyValue $login.Data 'data'
$token = Get-PropertyValue $loginData 'accessToken'
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Login yanıtında access token bulunamadı.' }
$headers = @{ Authorization = "Bearer $token"; 'X-Branch-Code' = $BranchCode }

$endpointList = @(Get-PagedEndpoints | Where-Object { $_ -like $EndpointPattern })
$serialId = $null
if ($endpointList -match '__SERIAL_ID__') {
    $serialSeed = Invoke-WmsPost '/api/stock-balances/serials/paged' (New-PagedBody $null $null) $headers
    $serialId = if ($serialSeed.Ok) {
        $serialSeedPage = Get-PageData $serialSeed.Data
        if ($serialSeedPage.Items.Count -gt 0) { Get-PropertyValue $serialSeedPage.Items[0] 'id' } else { $null }
    } else { $null }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($endpoint in $endpointList) {
    if ($endpoint.Contains('__SERIAL_ID__')) {
        if ($null -eq $serialId) {
            $results.Add([pscustomobject]@{
                Endpoint = $endpoint
                Total = $null
                Turkish = 'NOT_RUN'
                English = 'NOT_RUN'
                Status = 'BASELINE_ERROR'
                Detail = 'Seri hareketleri için örnek seri bakiyesi bulunamadı.'
            })
            continue
        }
        $endpoint = $endpoint.Replace('__SERIAL_ID__', [string] $serialId)
    }
    $baseline = Invoke-WmsPost $endpoint (New-PagedBody $null $null) $headers
    if (-not $baseline.Ok) {
        $results.Add([pscustomobject]@{
            Endpoint = $endpoint
            Total = $null
            Turkish = 'NOT_RUN'
            English = 'NOT_RUN'
            Status = 'BASELINE_ERROR'
            Detail = "HTTP $($baseline.Status): $($baseline.Error)"
        })
        continue
    }

    $page = Get-PageData $baseline.Data
    if ($page.Items.Count -eq 0) {
        $probe = Invoke-WmsPost $endpoint (New-PagedBody 'çğıöşü' $null) $headers
        $results.Add([pscustomobject]@{
            Endpoint = $endpoint
            Total = $page.TotalCount
            Turkish = if ($probe.Ok) { 'NO_DATA_ACCEPTED' } else { 'NO_DATA_ERROR' }
            English = 'NO_DATA'
            Status = 'EMPTY'
            Detail = if ($probe.Ok) { 'Endpoint boş; Türkçe input 200 döndü fakat veri eşleşmesi doğrulanamadı.' } else { $probe.Error }
        })
        continue
    }

    $turkishCandidates = @(Get-SearchCandidates $page.Items $true)
    $englishCandidates = @(Get-SearchCandidates $page.Items $false)
    $turkish = if ($turkishCandidates.Count -gt 0) {
        Test-SearchVariants $endpoint $turkishCandidates $headers 'Turkish'
    } else {
        $probe = Invoke-WmsPost $endpoint (New-PagedBody 'çğıöşü' $null) $headers
        [pscustomobject]@{
            Status = if ($probe.Ok) { 'NO_TURKISH_DATA' } else { 'HTTP_ERROR' }
            Field = $null
            Search = 'çğıöşü'
            Alternate = 'cgiosu'
            VariantCount = 0
            Detail = if ($probe.Ok) { 'İlk sayfada Türkçe karakterli gerçek örnek yok; input kabul edildi.' } else { $probe.Error }
        }
    }
    $english = Test-SearchVariants $endpoint $englishCandidates $headers 'English'

    $overall = if ($turkish.Status -eq 'MISMATCH' -or $english.Status -eq 'MISMATCH') { 'MISMATCH' }
        elseif ($turkish.Status -eq 'HTTP_ERROR' -or $english.Status -eq 'HTTP_ERROR') { 'SEARCH_ERROR' }
        elseif ($english.Status -eq 'PASS' -and $turkish.Status -in @('PASS', 'NO_TURKISH_DATA')) { 'PASS' }
        else { 'PARTIAL' }

    $results.Add([pscustomobject]@{
        Endpoint = $endpoint
        Total = $page.TotalCount
        Turkish = $turkish.Status
        English = $english.Status
        TurkishVariantCount = $turkish.VariantCount
        EnglishVariantCount = $english.VariantCount
        Status = $overall
        Detail = "TR[$($turkish.Field)] $($turkish.Search) => $($turkish.Alternate): $($turkish.Detail); EN[$($english.Field)] $($english.Search) => $($english.Alternate): $($english.Detail)"
    })
}

$summary = $results | Group-Object Status | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Status = $_.Name; Count = $_.Count }
}

[pscustomobject]@{
    BaseUrl = $BaseUrl
    TestedAtUtc = [DateTime]::UtcNow
    EndpointCount = $results.Count
    Summary = @($summary)
    Results = @($results)
} | ConvertTo-Json -Depth 8
