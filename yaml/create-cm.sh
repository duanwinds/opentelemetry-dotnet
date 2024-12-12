#!/bin/bash
kubectl -n o10y create cm appsettings --from-file=appsettings.custom.json=appsettings.custom.json
