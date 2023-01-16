using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Namotion.Reflection;
using Otus.Teaching.PromoCodeFactory.Core.Abstractions.Repositories;
using Otus.Teaching.PromoCodeFactory.Core.Domain.PromoCodeManagement;
using Otus.Teaching.PromoCodeFactory.WebHost.Controllers;
using Otus.Teaching.PromoCodeFactory.WebHost.Models;
using Xunit;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Model;

namespace Otus.Teaching.PromoCodeFactory.UnitTests.WebHost.Controllers.Partners
{
    public class CancelPartnerPromoCodeLimitAsyncTests
    {
        private readonly Mock<IRepository<Partner>> _partnersRepositoryMock;
        private readonly PartnersController _partnersController;
        
        public CancelPartnerPromoCodeLimitAsyncTests()
        {
            var fixture = new Fixture().Customize(new AutoMoqCustomization());
            _partnersRepositoryMock = fixture.Freeze<Mock<IRepository<Partner>>>();
            _partnersController = fixture.Build<PartnersController>().OmitAutoProperties().Create();
        }

        public Partner CreateBasePartner()
        {
            var partner = new Partner()
            {
                Id = Guid.Parse("7d994823-8226-4273-b063-1a95f3cc1df8"),
                Name = "Суперигрушки",
                IsActive = true,
                PartnerLimits = new List<PartnerPromoCodeLimit>()
                {
                    new PartnerPromoCodeLimit()
                    {
                        Id = Guid.Parse("e00633a5-978a-420e-a7d6-3e1dab116393"),
                        CreateDate = new DateTime(2020, 07, 9),
                        EndDate = new DateTime(2020, 10, 9),
                        Limit = 100
                    }
                }
            };

            return partner;
        }
        [Fact]
        public async void CancelPartnerPromoCodeLimitAsync_PartnerIsNotFound_ReturnsNotFound()
        {
            // Arrange
            var partnerId = Guid.Parse("def47943-7aaf-44a1-ae21-05aa4948b165");
            Partner partner = null;

            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId))
                .ReturnsAsync(partner);

            // Act
            var result = await _partnersController.CancelPartnerPromoCodeLimitAsync(partnerId);

            // Assert
            result.Should().BeAssignableTo<NotFoundResult>();
        }

        [Fact]
        public async void CancelPartnerPromoCodeLimitAsync_PartnerIsNotActive_ReturnsBadRequest()
        {
            // Arrange
            var partnerId = Guid.Parse("def47943-7aaf-44a1-ae21-05aa4948b165");
            var partner = CreateBasePartner();
            partner.IsActive = false;

            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId))
                .ReturnsAsync(partner);

            // Act
            var result = await _partnersController.CancelPartnerPromoCodeLimitAsync(partnerId);

            // Assert
            result.Should().BeAssignableTo<BadRequestObjectResult>();
        }

        //Если партнер не найден, то также нужно выдать ошибку 404;
        [Fact]
        public async System.Threading.Tasks.Task SetPartnerPromoCodeLimitAsync_PartnerIsNotFound_ReturnsError404()
        {
            // Arrange
            var partnerId = Guid.Parse("def47943-7aaf-44a1-ae21-05aa4948b165");
            Partner partner = null;

            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId))
                .ReturnsAsync(partner);
           // Act
            var result = await Assert.ThrowsAsync<ArgumentException>(async () => await _partnersController.SetPartnerPromoCodeLimitAsync(partnerId, null));
            Assert.Equal(404, Convert.ToInt16(result.ParamName));
        }

        [Fact]
        public async void SetPartnerPromoCodeLimitAsync_PartnerIsNotActive_ReturnsError400()
        {
            // Arrange
            var partnerId = Guid.Parse("def47943-7aaf-44a1-ae21-05aa4948b165");
            var partner = CreateBasePartner();
            partner.IsActive = false;
            
            // Act
            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId))
                .ReturnsAsync(partner);
            var result = await Assert.ThrowsAsync<ArgumentException>(async () => await _partnersController.SetPartnerPromoCodeLimitAsync(partnerId, null));
            
            // Assert
            Assert.Equal(400, Convert.ToInt32(result.ParamName));
        }

        //Если партнеру выставляется лимит, то мы должны обнулить количество промокодов, которые партнер выдал NumberIssuedPromoCodes, если лимит закончился,
        //то количество не обнуляется;
        [Fact]
        public async void SetPartnerPromoCodeLimitAsync_AddLimit_SetZerroNumberIssuedPromoCodes() 
        {
            // Arrange
            var partnerId = Guid.Parse("7d994823-8226-4273-b063-1a95f3cc1df8");  
            var partner = CreateBasePartner();
            SetPartnerPromoCodeLimitRequest newlimit = new SetPartnerPromoCodeLimitRequest() { EndDate = DateTime.Now, Limit = 15 };
            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId)).ReturnsAsync(partner);
            
            // Act
            var result = await _partnersController.SetPartnerPromoCodeLimitAsync(partner.Id, newlimit);
            
            // Assert
            Assert.Equal(0, partner.NumberIssuedPromoCodes);
            partner.NumberIssuedPromoCodes.Should().Be(0);
        }

        //При установке лимита нужно отключить предыдущий лимит;
        [Fact]
        public async void SetPartnerPromoCodeLimitAsync_SetPartnerLimits_activeLimitSetCancelDate()
        {
            // Arrange
            var partnerId = Guid.Parse("7d994823-8226-4273-b063-1a95f3cc1df8"); 
            var partner = CreateBasePartner();
            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId)).ReturnsAsync(partner);
            
            var activeLimit = partner.PartnerLimits.FirstOrDefault(x =>
               !x.CancelDate.HasValue);
            SetPartnerPromoCodeLimitRequest newlimit = new SetPartnerPromoCodeLimitRequest() { EndDate = DateTime.Now, Limit = 15 };
            // Act
            var result = await _partnersController.SetPartnerPromoCodeLimitAsync(partner.Id, newlimit);
            var after = partner.PartnerLimits.FirstOrDefault(x => x.CancelDate.HasValue);
            Assert.True(after.CancelDate.HasValue);
            Assert.NotNull(after.CancelDate);
        }

        //Лимит должен быть больше 0;
        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        public async void SetPartnerPromoCodeLimitAsync_SetPartnerLimits_limitMustBeGreaterThanZerro(int limit)
        {
            // Arrange
            var partnerId = Guid.Parse("7d994823-8226-4273-b063-1a95f3cc1df8");
            var partner = CreateBasePartner();
            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId)).ReturnsAsync(partner);
            SetPartnerPromoCodeLimitRequest newlimit = new SetPartnerPromoCodeLimitRequest() { EndDate = DateTime.Now, Limit = limit };
            // Act
            var result = await _partnersController.SetPartnerPromoCodeLimitAsync(partner.Id, newlimit);
            result.Should().BeAssignableTo<BadRequestObjectResult>();
            
        }

        //сохранили новый лимит в базу данных
        [Fact]
        public async void SetPartnerPromoCodeLimitAsync_SetPartnerLimits_NewLimitHasBeenSavedToTheDatabase()
        {
            // Arrange
            var partnerId = Guid.Parse("7d994823-8226-4273-b063-1a95f3cc1df8");  ////0da65561-cf56-4942-bff2-22f50cf70d43
            var partner = CreateBasePartner();
            
            _partnersRepositoryMock.Setup(repo => repo.GetByIdAsync(partnerId)).ReturnsAsync(partner);
            SetPartnerPromoCodeLimitRequest newlimit = new SetPartnerPromoCodeLimitRequest() { EndDate = DateTime.Now, Limit = 15 };            
            
            var before = partner.PartnerLimits.FirstOrDefault(x => x.Limit == newlimit.Limit && x.EndDate == newlimit.EndDate);           
            Assert.Null(before);

            // Act
            var result = await _partnersController.SetPartnerPromoCodeLimitAsync(partner.Id, newlimit);

            var after = partner.PartnerLimits.FirstOrDefault(x => x.Limit == newlimit.Limit && x.EndDate == newlimit.EndDate);
            
            // Assert
            Assert.NotNull(after);
            after.Should().NotBeNull();
        }
    }
}