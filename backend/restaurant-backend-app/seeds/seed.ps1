# Loads seeds/locations.json, dishes.json, tables.json, and waiters.json into DynamoDB.
# Does nothing to AWS unless you pass -Confirm.
# waiters.json "email" is for review only and is not written. userId is the Waiters hash key.
#
#   ./seed.ps1
#   ./seed.ps1 -Confirm
#
# dishType: "Appetizer" | "Main course" | "Dessert"  -> 0 | 1 | 2
# state:    "On stop" | "Available" | "Out of stock" -> 0 | 1 | 2

param(
    [string]$Region = "us-west-2",
    [switch]$Confirm
)

$ErrorActionPreference = "Stop"

$DishTypes = @{
    "Appetizer"   = 0
    "Main course" = 1
    "Dessert"     = 2
}

$DishStates = @{
    "On stop"      = 0
    "Available"    = 1
    "Out of stock" = 2
}

function Read-SeedArray {
    param([string]$Path)
    $parsed = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($null -eq $parsed) {
        return @()
    }
    return @($parsed)
}

function Format-DynamoNumber {
    param($Value)
    return [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0}", $Value)
}

function New-StringAttribute {
    param([string]$Value)
    return @{ S = $Value }
}

function New-NumberAttribute {
    param($Value)
    return @{ N = (Format-DynamoNumber $Value) }
}

function New-StringListAttribute {
    param([string[]]$Values)
    $entries = @()
    foreach ($value in $Values) {
        $entries += @{ S = $value }
    }
    return @{ L = $entries }
}

function Convert-LocationItem {
    param($Location)
    $item = @{
        id                = (New-StringAttribute $Location.id)
        address           = (New-StringAttribute $Location.address)
        description       = (New-StringAttribute $Location.description)
        totalCapacity     = (New-NumberAttribute $Location.totalCapacity)
        averageOccupancy  = (New-StringAttribute $Location.averageOccupancy)
        rating            = (New-NumberAttribute $Location.rating)
        specialtyDishIds  = (New-StringListAttribute @($Location.specialtyDishIds))
    }
    if (-not [string]::IsNullOrWhiteSpace($Location.imageUrl)) {
        $item.imageUrl = New-StringAttribute $Location.imageUrl
    }
    return $item
}

function Convert-DishItem {
    param($Dish)
    if (-not $DishTypes.ContainsKey($Dish.dishType)) {
        throw "Unknown dishType '$($Dish.dishType)' on $($Dish.id)."
    }
    if (-not $DishStates.ContainsKey($Dish.state)) {
        throw "Unknown state '$($Dish.state)' on $($Dish.id)."
    }
    $item = @{
        id            = (New-StringAttribute $Dish.id)
        locationId    = (New-StringAttribute $Dish.locationId)
        name          = (New-StringAttribute $Dish.name)
        description   = (New-StringAttribute $Dish.description)
        dishType      = (New-NumberAttribute $DishTypes[$Dish.dishType])
        price         = (New-NumberAttribute $Dish.price)
        weight        = (New-StringAttribute $Dish.weight)
        calories      = (New-StringAttribute $Dish.calories)
        proteins      = (New-StringAttribute $Dish.proteins)
        fats          = (New-StringAttribute $Dish.fats)
        carbohydrates = (New-StringAttribute $Dish.carbohydrates)
        vitamins      = (New-StringAttribute $Dish.vitamins)
        state         = (New-NumberAttribute $DishStates[$Dish.state])
    }
    if (-not [string]::IsNullOrWhiteSpace($Dish.imageUrl)) {
        $item.imageUrl = New-StringAttribute $Dish.imageUrl
    }
    return $item
}

function Convert-TableItem {
    param($Table)
    return @{
        id          = (New-StringAttribute $Table.id)
        locationId  = (New-StringAttribute $Table.locationId)
        tableNumber = (New-NumberAttribute $Table.tableNumber)
        capacity    = (New-NumberAttribute $Table.capacity)
    }
}

function Convert-WaiterItem {
    param($Waiter)
    $item = @{
        userId     = (New-StringAttribute $Waiter.userId)
        locationId = (New-StringAttribute $Waiter.locationId)
        createdAt  = (New-StringAttribute $Waiter.createdAt)
    }
    $reservations = @($Waiter.reservations)
    if ($reservations.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($reservations[0])) {
        $item.reservations = New-StringListAttribute $reservations
    }
    return $item
}

function Assert-SeedReferences {
    param($Locations, $Dishes, $Tables, $Waiters)

    $locationIds = @{}
    foreach ($location in $Locations) {
        if ($locationIds.ContainsKey($location.id)) {
            throw "Duplicate location id '$($location.id)'."
        }
        $locationIds[$location.id] = $true
    }

    $dishIds = @{}
    foreach ($dish in $Dishes) {
        if ($dishIds.ContainsKey($dish.id)) {
            throw "Duplicate dish id '$($dish.id)'."
        }
        $dishIds[$dish.id] = $dish.locationId
        if (-not $locationIds.ContainsKey($dish.locationId)) {
            throw "Dish '$($dish.id)' points at missing location '$($dish.locationId)'."
        }
    }

    foreach ($location in $Locations) {
        foreach ($dishId in @($location.specialtyDishIds)) {
            if (-not $dishIds.ContainsKey($dishId)) {
                throw "Location '$($location.id)' lists missing specialty dish '$dishId'."
            }
            if ($dishIds[$dishId] -ne $location.id) {
                throw "Specialty dish '$dishId' does not belong to location '$($location.id)'."
            }
        }
    }

    $tableKeys = @{}
    foreach ($table in $Tables) {
        if (-not $locationIds.ContainsKey($table.locationId)) {
            throw "Table '$($table.id)' points at missing location '$($table.locationId)'."
        }
        $key = "$($table.locationId):$($table.tableNumber)"
        if ($tableKeys.ContainsKey($key)) {
            throw "Duplicate table number $($table.tableNumber) at location '$($table.locationId)'."
        }
        $tableKeys[$key] = $true
    }

    $waiterIds = @{}
    foreach ($waiter in $Waiters) {
        if ([string]::IsNullOrWhiteSpace($waiter.userId)) {
            throw "A waiter is missing userId."
        }
        if ($waiterIds.ContainsKey($waiter.userId)) {
            throw "Duplicate waiter userId '$($waiter.userId)'."
        }
        $waiterIds[$waiter.userId] = $true
        if (-not $locationIds.ContainsKey($waiter.locationId)) {
            throw "Waiter '$($waiter.userId)' points at missing location '$($waiter.locationId)'."
        }
        if ([string]::IsNullOrWhiteSpace($waiter.createdAt)) {
            throw "Waiter '$($waiter.userId)' is missing createdAt."
        }
    }
}

function Write-SeedBatch {
    param(
        [string]$TableName,
        [array]$Items
    )

    for ($offset = 0; $offset -lt $Items.Count; $offset += 25) {
        $count = [Math]::Min(25, $Items.Count - $offset)
        $puts = @()
        for ($index = 0; $index -lt $count; $index++) {
            $puts += @{ PutRequest = @{ Item = $Items[$offset + $index] } }
        }

        $request = @{ $TableName = $puts }
        $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("green-tasty-seed-" + [guid]::NewGuid().ToString() + ".json")
        $json = $request | ConvertTo-Json -Depth 12 -Compress
        [System.IO.File]::WriteAllText($tempFile, $json)

        try {
            aws dynamodb batch-write-item --region $Region --request-items "file://$tempFile"
            if ($LASTEXITCODE -ne 0) {
                throw "batch-write-item failed for $TableName (exit $LASTEXITCODE)."
            }
        }
        finally {
            Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
        }
    }
}

$seedRoot = $PSScriptRoot
$locations = Read-SeedArray (Join-Path $seedRoot "locations.json")
$dishes = Read-SeedArray (Join-Path $seedRoot "dishes.json")
$tables = Read-SeedArray (Join-Path $seedRoot "tables.json")
$waiters = Read-SeedArray (Join-Path $seedRoot "waiters.json")

Assert-SeedReferences -Locations $locations -Dishes $dishes -Tables $tables -Waiters $waiters

Write-Host "Seeds: $($locations.Count) locations, $($dishes.Count) dishes, $($tables.Count) tables, $($waiters.Count) waiters."
Write-Host "Target tables: Locations, Dishes, Tables, Waiters in $Region."

if (-not $Confirm) {
    Write-Host "Nothing was uploaded. Re-run with -Confirm to write these items."
    exit 0
}

Write-SeedBatch -TableName "Locations" -Items @($locations | ForEach-Object { Convert-LocationItem $_ })
Write-SeedBatch -TableName "Dishes" -Items @($dishes | ForEach-Object { Convert-DishItem $_ })
Write-SeedBatch -TableName "Tables" -Items @($tables | ForEach-Object { Convert-TableItem $_ })
Write-SeedBatch -TableName "Waiters" -Items @($waiters | ForEach-Object { Convert-WaiterItem $_ })

Write-Host "Upload finished."
