@echo off

..\..\packages\Google.Protobuf.Tools.3.6.0\tools\windows_x64\protoc --csharp_out=.. --proto_path=. protocol.proto
