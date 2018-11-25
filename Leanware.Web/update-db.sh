#!/bin/bash
dotnet ef migrations add `date "+%m%d%Y%H%M%S"` -c LeanwareContext -o Data/Migrations