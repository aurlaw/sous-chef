.PHONY: dev migrate

dev: ## Start full local environment
	aspire start

stop: ## Stop full local environment
	aspire stop

migrate: ## Run EF Core migrations manually
	dotnet ef database update \
		--project SousChef.Infrastructure \
		--startup-project SousChef.Api
