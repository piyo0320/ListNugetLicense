# ListNugetLicense

Nugetで取得したOSSのライセンスファイルを取得するツールです。

# 使い方

1. パッケージマネージャコンソールで以下のコマンドを実行し、出力結果を「NugetPackageList.txt」にコピペします。
Get-Package | Select-Object Id, Version | Foreach-Object { "$($_.Id)`t$($_.Version)" }

2. 必要に応じて、appsetting.jsonの「OutputFolderPath」に、出力先のフォルダパスを記載します。
指定しなかった場合、ビルドされたバイナリと同じフォルダ(bin/Debugなど)の直下にライセンスファイルを生成します。

3. 検証はしていませんが、プロキシ環境で使う場合はappsetting.jsonのProxyの部分に必要事項を記載すれば動くのではないかと思っています････
