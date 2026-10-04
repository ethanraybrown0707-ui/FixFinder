# The tests that run a class of JUnit tests need JUnit itself, and the test of a program that uses Lombok needs Lombok; no
# runner image has either. They are fetched from Maven Central and checked against the SHA-1 Maven Central publishes beside
# each jar. The tests find them through FIXFINDER_JUNIT_JARS and FIXFINDER_LOMBOK_JAR, and return early without them, as the
# tests that need a missing compiler do.
#
# Lombok 1.18.48 is the first to support JDK 27, as Lombok's changelog records, so the same jar serves every JDK the tests
# run with.

$ErrorActionPreference = 'Stop'

$folder = Join-Path $env:RUNNER_TEMP "junit"
New-Item -ItemType Directory -Force -Path $folder | Out-Null

$jars = @(
  "org/junit/platform/junit-platform-console-standalone/1.11.4/junit-platform-console-standalone-1.11.4.jar",
  "junit/junit/4.13.2/junit-4.13.2.jar",
  "org/hamcrest/hamcrest-core/1.3/hamcrest-core-1.3.jar",
  "org/projectlombok/lombok/1.18.48/lombok-1.18.48.jar"
)

foreach ($jar in $jars) {
  $url = "https://repo1.maven.org/maven2/$jar"
  $file = Join-Path $folder (Split-Path $jar -Leaf)
  Invoke-WebRequest -Uri $url -OutFile $file
  Invoke-WebRequest -Uri "$url.sha1" -OutFile "$file.sha1"

  $published = (Get-Content "$file.sha1" -Raw).Trim().Split(' ')[0].ToUpperInvariant()
  $downloaded = (Get-FileHash $file -Algorithm SHA1).Hash
  if ($published -ne $downloaded) { throw "$jar does not match the SHA-1 Maven Central publishes for it" }
  Remove-Item "$file.sha1"
}

# Lombok is moved out of the JUnit folder, so the tests that run JUnit see only what they did before.
$lombok = Join-Path $env:RUNNER_TEMP "lombok"
New-Item -ItemType Directory -Force -Path $lombok | Out-Null
Move-Item (Join-Path $folder "lombok-1.18.48.jar") $lombok

"FIXFINDER_JUNIT_JARS=$folder" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
"FIXFINDER_LOMBOK_JAR=$(Join-Path $lombok 'lombok-1.18.48.jar')" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
