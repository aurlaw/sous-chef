.PHONY: dev migrate

dev: ## Start full local environment
	aspire start

migrate: ## Run EF Core migrations manually
	dotnet ef database update \
		--project SousChef.Infrastructure \
		--startup-project SousChef.Api
