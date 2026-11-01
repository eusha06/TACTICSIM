#!/bin/sh
set -eu

# Only serve requests after the reviewed migrations succeed.
dotnet TacticSim.Web.dll --migrate "$@"
exec dotnet TacticSim.Web.dll "$@"
